using AusbildungsWegInterview.Core.Answers;

namespace AusbildungsWegInterview.Answers;

/// <summary>
/// Heuristic question detector. It is intentionally conservative: a finalized
/// segment that is long enough is treated as answerable, while a provisional
/// segment must also look like a question (a "?" or a question word) so the app
/// does not fire answers on fragments.
///
/// This is a fast, deterministic pre-gate. A model-based detector can replace it
/// later by implementing <see cref="IQuestionDetector"/>.
/// </summary>
public sealed class QuestionDetector : IQuestionDetector
{
    private static readonly string[] Interrogatives =
    {
        // English
        "who", "what", "why", "when", "where", "which", "whom", "whose", "how",
        // German
        "wer", "was", "warum", "wann", "wo", "wie", "welche", "welchen", "welches", "welcher",
        // French
        "qui", "quoi", "pourquoi", "comment", "quand", "ou", "quelle", "quel", "combien",
        // Arabic
        "مَن", "ماذا", "لماذا", "متى", "اين", "كيف", "ما"
    };

    public bool IsSufficient(string transcript, int minChars, bool isFinal)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return false;

        var text = transcript.Trim();
        if (text.Length < minChars)
            return false;

        if (text.Contains('?'))
            return true;

        if (isFinal)
            return true;

        return HasInterrogative(text);
    }

    private static bool HasInterrogative(string text)
    {
        foreach (var token in Tokenize(text))
        {
            if (Interrogatives.Contains(token, StringComparer.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var current = new System.Text.StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                current.Append(ch);
            }
            else if (current.Length > 0)
            {
                yield return current.ToString();
                current.Clear();
            }
        }
        if (current.Length > 0)
            yield return current.ToString();
    }
}
