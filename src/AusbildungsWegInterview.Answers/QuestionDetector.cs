using AusbildungsWegInterview.Core.Answers;

namespace AusbildungsWegInterview.Answers;

/// <summary>
/// Practical, multilingual question detector that works on INCOMPLETE transcript
/// segments. It distinguishes a genuine interview question from an ordinary
/// statement, filler, or an incomplete fragment:
///  * A "?" (ASCII or Arabic "؟") is a question cue at any length.
///  * A known question phrase (interrogative or imperative interview pattern)
///    is a question cue in all four supported languages (DE/EN/FR/AR).
///  * An interrogative word ("why/warum/pourquoi/لماذا", ...) is a cue for
///    provisional text and, for finalized text, a second-level cue.
///  * A finalized segment WITHOUT any interrogative cue is treated as a
///    statement, NOT as a question: the transcript stays visible, translation
///    keeps working, but no answer is generated (the interviewer's fillers and
///    remarks must not trigger suggestions).
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
        "لماذا", "كيف", "ماذا", "متى", "من", "مَن", "ما", "هل"
    };

    private static readonly string[] Phrases =
    {
        // English
        "could you", "can you", "would you", "tell me", "talk to me about", "talk about",
        "walk me through", "describe", "explain", "give me", "where do you see", "what are you looking for",
        "what do you", "why do you", "what would", "why should",
        // German (incl. unaccented variants ASR commonly produces)
        "koennen sie", "können sie", "erzaehlen sie", "erzählen sie", "sagen sie", "reden sie", "sprechen sie",
        "beschreiben sie", "nennen sie", "erzaehl mir", "erzähl mir", "sagen sie mir", "wie ist es",
        "wie sieht es", "wo stehen sie", "wo sehen sie", "was erwartet", "was ist", "warum wollen", "wie sind",
        // French (incl. unaccented variants)
        "qu'est-ce que", "qu est ce que", "pouvez-vous", "pouvez vous", "parlez-moi", "parlez de",
        "parle-moi", "decrivez", "décrivez", "expliquez", "racontez", "est-ce que", "ou vous voyez",
        "où vous voyez", "pourquoi avez",
        // Arabic
        "هل", "ما رأيك", "لماذا تريد", "حدثني", "أخبرني", "اخبرني", "اشرح لي", "صف لي", "عن نفسك",
        "ما هو", "ما هي", "كيف ترى"
    };

    public bool IsSufficient(string transcript, int minChars, bool isFinal)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return false;

        var text = CollapseWhitespace(transcript);
        if (text.Length < minChars)
            return false;

        // Strong cues: question mark or a known question phrase (any state).
        if (text.Contains('?') || text.Contains('؟'))
            return true;
        if (HasPhrase(text))
            return true;

        // Weaker cue: an interrogative word. Required for provisional text;
        // for finalized text it is what separates a question from a plain
        // statement/filler — no cue, no answer.
        return HasWord(text);
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
