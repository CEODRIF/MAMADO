using System.Text;

namespace AusbildungsWegInterview.App;

/// <summary>Completion state of an access-code entry (UI feedback only).</summary>
public enum AccessCodeState
{
    /// <summary>Nothing entered yet.</summary>
    Empty,

    /// <summary>Some, but fewer than 16, characters entered.</summary>
    Partial,

    /// <summary>All 16 characters entered (display format is complete).</summary>
    Valid
}

/// <summary>
/// Pure (WPF-free) formatting/validation for the <c>XXXX-XXXX-XXXX-XXXX</c>
/// access-code field: normalization (letters A–Z + digits only, upper-cased,
/// capped at 16), automatic four-group formatting, and completion state.
///
/// IMPORTANT (security honesty): this is INPUT FORMATTING ONLY. No universal
/// secret exists here, nothing is checked against an authorization backend,
/// and none of this grants or verifies access. The code value is never
/// persisted or logged by the application.
/// </summary>
public static class AccessCodeFormatter
{
    public const int MaxCharacters = 16;
    public const int GroupSize = 4;
    public const string DisplayPattern = "XXXX-XXXX-XXXX-XXXX";

    /// <summary>
    /// Keeps only A–Z (case-insensitive) and 0–9 characters, upper-cased,
    /// at most <see cref="MaxCharacters"/> of them. Dashes, spaces, and all
    /// other symbols are stripped, so paste and typing both behave.
    /// </summary>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var sb = new StringBuilder(MaxCharacters);
        foreach (char c in input)
        {
            if (sb.Length >= MaxCharacters)
                break;
            if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9')
                sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>Groups the normalized code into four blocks of four (last block may be shorter).</summary>
    public static string Format(string normalizedInput)
    {
        var raw = Normalize(normalizedInput);
        if (raw.Length == 0)
            return string.Empty;

        var sb = new StringBuilder(raw.Length + GroupSize - 1);
        for (int i = 0; i < raw.Length; i += GroupSize)
        {
            if (i > 0)
                sb.Append('-');
            sb.Append(raw, i, Math.Min(GroupSize, raw.Length - i));
        }
        return sb.ToString();
    }

    /// <summary>Normalize + format in one step (the TextBox binding path).</summary>
    public static string FormatInput(string? input) => Format(Normalize(input));

    /// <summary>Raw (unformatted) character count of the input, for progress feedback.</summary>
    public static int Length(string? input) => Normalize(input).Length;

    /// <summary>Completion state for the raw (unformatted) code length.</summary>
    public static AccessCodeState State(string? input) => Normalize(input).Length switch
    {
        0 => AccessCodeState.Empty,
        MaxCharacters => AccessCodeState.Valid,
        _ => AccessCodeState.Partial
    };

    /// <summary>
    /// Strict check for the canonical display format (used by tests and by any
    /// future programmatic entry). Formatting via <see cref="FormatInput"/>
    /// always produces a string that passes this check when complete.
    /// </summary>
    public static bool IsValidDisplayFormat(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length != MaxCharacters + 3)
            return false;

        for (int i = 0; i < text.Length; i++)
        {
            if (i % 5 == 4)
            {
                if (text[i] != '-')
                    return false;
            }
            else
            {
                char c = text[i];
                if (!(c is >= 'A' and <= 'Z' or >= '0' and <= '9'))
                    return false;
            }
        }
        return true;
    }
}