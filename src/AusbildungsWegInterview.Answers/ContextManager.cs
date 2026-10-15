using System.Text;
using AusbildungsWegInterview.Core.Answers;
using AusbildungsWegInterview.Core.Common;

namespace AusbildungsWegInterview.Answers;

/// <summary>
/// Builds grounded, honest answer prompts from the question, candidate material,
/// bounded recent conversation, and the requested style/length.
///
/// Grounding guarantees (enforced in the prompt):
///  * Only facts explicitly present in [CV]/[Job description] may be asserted.
///  * No invented qualifications, history, proficiency, achievements, or experiences.
///  * Unsupported specifics become a neutral general answer or a clarifying question.
///  * Prompts are bounded (truncation) to avoid unbounded context growth.
/// </summary>
public static class ContextManager
{
    public static string BuildPrompt(AnswerRequest request)
    {
        var sb = new StringBuilder();
        var lang = LanguageNames.DisplayName(request.AnswerLanguage);

        sb.AppendLine("You are a helpful, honest assistant helping a job / apprenticeship (Ausbildung) candidate answer interview questions.");
        sb.AppendLine("Tone: " + StyleInstruction(request.Style) + ". Length: " + LengthInstruction(request.Length) + ".");
        sb.AppendLine("Write the answer in " + lang + ", in first person, in a natural spoken style.");
        sb.AppendLine();
        sb.AppendLine("STRICT HONESTY RULES:");
        sb.AppendLine("- You may assert ONLY facts explicitly present in the verified candidate material below.");
        sb.AppendLine("- NEVER invent qualifications, employment history, employers, dates, numbers, language proficiency, achievements, awards, or personal experiences.");
        sb.AppendLine("- If the material does not support a specific claim, do NOT fill it in: give a neutral, general answer, or say the candidate would gladly elaborate, or ask one short clarifying question.");
        sb.AppendLine();

        sb.AppendLine("Question (original): " + request.QuestionOriginal);
        if (!string.IsNullOrWhiteSpace(request.QuestionTranslated))
            sb.AppendLine("Question (translated): " + request.QuestionTranslated);

        if (request.HasRecentContext)
        {
            sb.AppendLine();
            sb.AppendLine("Recent conversation (context only; do not quote it verbatim):");
            foreach (var turn in request.RecentContext!)
                sb.AppendLine("  " + turn);
        }

        sb.AppendLine();
        if (request.HasContext)
        {
            sb.AppendLine("--- VERIFIED candidate material (local, user-provided) ---");
            if (!string.IsNullOrWhiteSpace(request.Context.CvText))
            {
                sb.AppendLine("[CV]");
                sb.AppendLine(Truncate(request.Context.CvText!, 4000));
            }
            if (!string.IsNullOrWhiteSpace(request.Context.JobDescription))
            {
                sb.AppendLine("[Job description]");
                sb.AppendLine(Truncate(request.Context.JobDescription!, 3000));
            }
            sb.AppendLine("--- END verified material ---");
        }
        else
        {
            sb.AppendLine("No candidate material was provided. Keep the answer general and do NOT fabricate any specific fact, name, number, or experience.");
        }

        sb.AppendLine();
        sb.AppendLine("Suggested answer:");
        return sb.ToString();
    }

    private static string LengthInstruction(AnswerLength length) => length switch
    {
        AnswerLength.Concise => "1-2 short sentences.",
        AnswerLength.Normal => "3-4 sentences.",
        AnswerLength.Detailed => "A short structured answer: 2-3 sentences plus one concrete, supported example.",
        _ => "3-4 sentences."
    };

    private static string StyleInstruction(AnswerStyle style) => style switch
    {
        AnswerStyle.Concise => "concise and direct, straight to the point.",
        AnswerStyle.Natural => "natural and conversational, like a real interview answer.",
        AnswerStyle.Professional => "professional, polished, and well-structured.",
        _ => "natural and conversational."
    };

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";
}
