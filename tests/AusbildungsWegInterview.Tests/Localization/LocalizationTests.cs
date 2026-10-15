using System.Globalization;
using System.Resources;
using AusbildungsWegInterview.Localization;
using Xunit;

namespace AusbildungsWegInterview.Tests.Localization;

/// <summary>
/// Tests for the static UI string catalogs (en baseline + de/fr/ar) and the
/// runtime language manager: coverage, fallbacks, RTL detection, and live
/// language switching. The VM-side re-binding (WPF) is not testable off-Windows;
/// everything in this project is.
/// </summary>
public class LocalizationTests
{
    private static readonly string[] Cultures = { "en", "de", "fr", "ar" };

    // (culture|key) pairs whose correct translation is the same word as the English
    // key - legitimate loanwords, not missing translations.
    private static readonly HashSet<string> LegitimatelySameAsKey = new()
    {
        "fr|Style"
    };

    private static string Get(string culture, string key)
    {
        var rm = new ResourceManager("AusbildungsWegInterview.Localization.Loc.Strings",
            typeof(LocalizationManager).Assembly);
        return rm.GetString(key, CultureInfo.GetCultureInfo(culture))!;
    }

    [Fact]
    public void EveryBaselineKey_IsTranslated_InAllCultures()
    {
        var keys = LocalizationManager.BaselineKeys();
        Assert.True(keys.Count >= 70, $"expected a full catalog, got {keys.Count} keys");

        foreach (var key in keys)
        {
            foreach (var c in Cultures)
            {
                var v = Get(c, key);
                Assert.False(string.IsNullOrWhiteSpace(v), $"key '{key}' is empty in {c}");
                // In the English baseline a value may legitimately equal the key
                // ("Download", "Refresh", ...); the other catalogs must not.
                if (c != "en" && !LegitimatelySameAsKey.Contains(c + "|" + key))
                    Assert.True(v != key, $"key '{key}' in {c} equals the English key (untranslated)");
            }
        }
    }

    [Fact]
    public void AllCultures_HaveExactlyTheSameKeySet()
    {
        var baseline = LocalizationManager.KeysForCulture("en").ToHashSet(StringComparer.Ordinal);
        foreach (var c in new[] { "de", "fr", "ar" })
        {
            var keys = LocalizationManager.KeysForCulture(c).ToHashSet(StringComparer.Ordinal);
            Assert.Equal(baseline, keys);
        }
    }

    [Fact]
    public void SpotChecks_AreGenuineTranslations_NotFallbacks()
    {
        // These must differ from English (proves per-culture catalogs, not fallback).
        Assert.NotEqual(Get("en", "StartListening"), Get("de", "StartListening"));
        Assert.NotEqual(Get("en", "StartListening"), Get("fr", "StartListening"));
        Assert.NotEqual(Get("en", "StartListening"), Get("ar", "StartListening"));
        Assert.Equal("Aufnahme starten", Get("de", "StartListening"));
        Assert.Equal("Démarrer l'écoute", Get("fr", "StartListening"));
        Assert.Equal("ابدأ الاستماع", Get("ar", "StartListening"));
    }

    [Fact]
    public void Manager_UnknownKey_FallsBackToKeyItself()
    {
        var loc = new LocalizationManager("en");
        Assert.Equal("No.Such.Key", loc["No.Such.Key"]);
    }

    [Fact]
    public void Manager_InvalidCulture_FallsBackToEnglish()
    {
        var loc = new LocalizationManager("xx");
        Assert.Equal("en", loc.Culture.TwoLetterISOLanguageName);
        Assert.False(loc.IsRtl);
        Assert.Equal("Start listening", loc.Get("StartListening")); // xx -> en baseline
    }

    [Fact]
    public void IsRtl_IsTrueOnlyForArabic()
    {
        foreach (var c in Cultures)
        {
            var loc = new LocalizationManager(c);
            Assert.True(loc.IsRtl == (c == "ar"), $"IsRtl wrong for {c}");
        }
    }

    [Fact]
    public void SetCulture_RaisesLanguageChanged_OnlyWhenChanged()
    {
        var loc = new LocalizationManager("en");
        int events = 0;
        loc.LanguageChanged += (_, _) => events++;

        loc.SetCulture("en"); // same culture: no event
        Assert.Equal(0, events);

        loc.SetCulture("de");
        Assert.Equal(1, events);
        Assert.Equal("de", loc.Culture.TwoLetterISOLanguageName);
        Assert.Equal("Aufnahme starten", loc["StartListening"]);

        loc.SetCulture("ar");
        Assert.Equal(2, events);
        Assert.True(loc.IsRtl);
        Assert.Equal("ابدأ الاستماع", loc["StartListening"]);
    }

    [Fact]
    public void F_FormatsWithArguments_InEveryCulture()
    {
        foreach (var c in Cultures)
        {
            var loc = new LocalizationManager(c);
            var s = loc.F("AiModelReady", "test-model");
            Assert.Contains("test-model", s);
            Assert.DoesNotContain("{0}", s);
        }
    }

    [Fact]
    public void UiLanguages_DeclaresExactlyArDeEnFr()
    {
        var codes = UiLanguages.All.Select(l => l.Code).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "ar", "de", "en", "fr" }, codes);
        Assert.All(UiLanguages.All, l => Assert.False(string.IsNullOrWhiteSpace(l.NativeName)));
    }
}
