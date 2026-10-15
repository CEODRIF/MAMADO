using AusbildungsWegInterview.Core.Answers;

namespace AusbildungsWegInterview.Answers;

/// <summary>
/// Practical, multilingual question detector that works on INCOMPLETE transcript
/// segments. It distinguishes an interview question from an ordinary statement or
/// an incomplete fragment, and is deliberately conservative for provisional text:
///  * A "?" (ASCII or Arabic "؟") or a known question phrase is sufficient at any length.
///  * A finalized segment that is long enough is treated as answerable.
///  * A provisional segment additionally needs an interrogative word.
///
/// Deterministic and fast; a model-based detector can replace it via IQuestionDetector.
/// </summary>
public sealed class QuestionDetector : IQuestionDetector
{
    private static readonly string[] Words =
    {
        // English
        "why", "how", "what", "which", "who", "when", "where", "whom", "whose", "could", "can", "would",
        // German
        "warum", "weshalb", "wie", "was", "welche", "welchen", "welches", "welcher", "wer", "wann", "wo",
        // French
        "pourquoi", "comment", "quel", "quelle", "quels", "quelles", "quand", "qui", "combien",
        // Arabic
        "لماذا", "كيف", "ماذا", "متى", "من", "مَن", "ما"
    };

    private static readonly string[] Phrases =
    {
        // English
        "could you", "can you", "tell me about", "what do you", "why do you", "what would", "why should",
        // German
        "koennen sie", "können sie", "erzaehlen sie", "erzählen sie", "was ist", "warum wollen", "wie sind",
        // French
        "qu'est-ce que", "qu est ce que", "pouvez-vous", "pouvez vous", "parlez-moi", "est-ce que",
        // Arabic
        "هل", "ما رأيك", "لماذا تريد", "حدثني"
    };

    public bool IsSufficient(string transcript, int minChars, bool isFinal)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return false;

        var text = CollapseWhitespace(transcript);
        if (text.Length < minChars)
            return false;

        if (text.Contains('?') || text.Contains('؟'))
            return true;
        if (HasPhrase(text))
            return true;
        if (isFinal)
            return true; // finalized + long enough
        return HasWord(text);   // provisional: require an interrogative cue
    }

    private static bool HasPhrase(string normalized)
        => Phrases.Any(p => normalized.Contains(p, StringComparison.OrdinalIgnoreCase));

    private static bool HasWord(string normalized)
    {
        foreach (var token in Tokenize(normalized))
        {
            if (Words.Contains(token, StringComparer.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string CollapseWhitespace(string text)
        => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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
