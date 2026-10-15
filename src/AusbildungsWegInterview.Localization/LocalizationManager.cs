using System.Globalization;
using System.Resources;

namespace AusbildungsWegInterview.Localization;

/// <summary>The UI languages supported by the app (native display names).</summary>
public static class UiLanguages
{
    public static readonly IReadOnlyList<(string Code, string NativeName)> All = new (string Code, string NativeName)[]
    {
        ("ar", "العربية"),
        ("de", "Deutsch"),
        ("en", "English"),
        ("fr", "Français")
    };

    public static bool IsValid(string? code) => All.Any(l => l.Code == code);
}

/// <summary>
/// Runtime UI language manager backed by static resource catalogs
/// (Loc/Strings.resx + per-culture files). No runtime machine translation.
///
/// Culture switching is live: consumers subscribe to <see cref="LanguageChanged"/>
/// and re-read through <see cref="Get"/> / the indexer.
/// </summary>
public sealed class LocalizationManager
{
    private static readonly ResourceManager Rm =
        new("AusbildungsWegInterview.Localization.Loc.Strings", typeof(LocalizationManager).Assembly);

    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en");

    public event EventHandler? LanguageChanged;

    public LocalizationManager(string cultureCode = "en")
        => Apply(cultureCode, raise: false);

    public void SetCulture(string cultureCode)
        => Apply(cultureCode, raise: true);

    private void Apply(string cultureCode, bool raise)
    {
        var target = CultureInfo.GetCultureInfo(UiLanguages.IsValid(cultureCode) ? cultureCode : "en");
        var changed = Culture.TwoLetterISOLanguageName != target.TwoLetterISOLanguageName;
        Culture = target;
        if (raise && changed)
            LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>True for right-to-left UI languages (Arabic).</summary>
    public bool IsRtl => Culture.TextInfo.IsRightToLeft;

    /// <summary>Localizes <paramref name="key"/>; falls back to the English catalog, then to the key itself.</summary>
    public string this[string key] => Get(key);

    public string Get(string key)
        => Rm.GetString(key, Culture) ?? Rm.GetString(key, CultureInfo.GetCultureInfo("en")) ?? key;

    /// <summary>Localizes and formats with the current culture (for {0} placeholders).</summary>
    public string F(string key, params object[] args)
        => args is { Length: > 0 } ? string.Format(Culture, Get(key), args) : Get(key);

    /// <summary>All keys of a culture catalog (neutral for "en"). For tests/tooling.</summary>
    public static IReadOnlyList<string> KeysForCulture(string cultureCode)
    {
        // Neutral catalog is embedded in the main assembly; culture catalogs ship as
        // satellite assemblies. Enumerate each via the matching set.
        var asm = typeof(LocalizationManager).Assembly;
        var names = new List<string>();
        if (cultureCode == "en")
        {
            var resName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(".Loc.Strings.resources", StringComparison.OrdinalIgnoreCase));
            using var stream = resName is null ? null : asm.GetManifestResourceStream(resName);
            if (stream is not null)
            {
                using var reader = new ResourceReader(stream);
                var e = reader.GetEnumerator();
                while (e.MoveNext())
                {
                    if (e.Key is string k && k.Length > 0)
                        names.Add(k);
                }
            }
        }
        else
        {
            var set = Rm.GetResourceSet(CultureInfo.GetCultureInfo(cultureCode), createIfNotExists: true, tryParents: false);
            if (set is not null)
            {
                foreach (System.Collections.DictionaryEntry entry in set)
                {
                    if (entry.Key is string k && k.Length > 0)
                        names.Add(k);
                }
            }
        }
        return names.OrderBy(n => n).ToList();
    }

    /// <summary>All keys of the English baseline catalog (for tests/tooling).</summary>
    public static IReadOnlyList<string> BaselineKeys() => KeysForCulture("en");
}
