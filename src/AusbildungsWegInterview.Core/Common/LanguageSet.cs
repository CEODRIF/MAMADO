namespace AusbildungsWegInterview.Core.Common;

/// <summary>
/// Parses the comma/semicolon-separated language lists used by the UI panels
/// (question translations + answer languages). Codes are trimmed, lower-cased,
/// de-duplicated and blank entries are dropped, so a malformed setting can
/// never produce an empty or duplicated panel.
/// </summary>
public static class LanguageSet
{
    private static readonly char[] Separators = { ',', ';', '|', ' ', '\t' };

    /// <summary>
    /// Parses <paramref name="list"/> and appends any <paramref name="required"/>
    /// codes that are missing (e.g. the configured primary language), preserving
    /// first-seen order.
    /// </summary>
    public static IReadOnlyList<string> Parse(string? list, params string?[] required)
    {
        var result = new List<string>();

        if (!string.IsNullOrWhiteSpace(list))
        {
            foreach (var part in list!.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
                Add(result, part);
        }

        foreach (var code in required)
            Add(result, code);

        return result;
    }

    /// <summary>
    /// Moves <paramref name="primary"/> to the front (the mockup shows the
    /// generated answer first). Unknown/blank primary leaves the order as-is.
    /// </summary>
    public static IReadOnlyList<string> WithPrimaryFirst(IReadOnlyList<string> languages, string? primary)
    {
        var p = Normalize(primary);
        if (p is null || languages.Count == 0 || languages[0] == p)
            return languages;

        var ordered = new List<string>(languages.Count) { p };
        foreach (var l in languages)
        {
            if (!string.Equals(l, p, StringComparison.OrdinalIgnoreCase))
                ordered.Add(l);
        }
        return ordered;
    }

    /// <summary>Trims/lower-cases a code; returns null for blank input.</summary>
    public static string? Normalize(string? code)
    {
        var c = code?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(c) ? null : c;
    }

    private static void Add(List<string> target, string? code)
    {
        var c = Normalize(code);
        if (c is not null && !target.Contains(c, StringComparer.OrdinalIgnoreCase))
            target.Add(c);
    }
}
