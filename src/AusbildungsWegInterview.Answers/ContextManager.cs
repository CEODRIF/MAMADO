using System.Text;
using AusbildungsWegInterview.Core.Answers;
using AusbildungsWegInterview.Core.Common;

namespace AusbildungsWegInterview.Answers;

/// <summary>
/// Builds grounded answer prompts from the question and optional candidate
/// material. Enforces the "do not invent facts" rule: when the CV/job
/// description does not support a claim, the model is instructed to stay
/// cautious or ask for clarification.
/// </summary>
public static class ContextManager
{
    public static string BuildPrompt(AnswerRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a helpful, honest interview assistant for a job / apprenticeship (Ausbildung) applicant.");
        sb.AppendLine("You must answer ONLY using facts supported by the candidate material below.");
        sb.AppendLine("If the material does not support a claim, do NOT invent specifics. Instead give a cautious, general answer or a short clarifying question.");
        sb.AppendLine("Write the answer in first person, in a natural spoken style, using the requested language.");
        sb.AppendLine();
        sb.AppendLine("Question (original): " + request.QuestionOriginal);
        if (!string.IsNullOrWhiteSpace(request.QuestionTranslated))
            sb.AppendLine("Question (translated): " + request.QuestionTranslated);
        sb.AppendLine();
        sb.AppendLine("Answer language: " + LanguageNames.DisplayName(request.AnswerLanguage));
        sb.AppendLine("Desired length: " + LengthInstruction(request.Length));
        sb.AppendLine();

        if (request.HasContext)
        {
            sb.AppendLine("--- Candidate material (local, provided by the user) ---");
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
            sb.AppendLine("--- End candidate material ---");
        }
        else
        {
            sb.AppendLine("No candidate material was provided. Keep the answer general and do NOT fabricate specific facts, names, or numbers.");
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

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..max] + "…";
}
