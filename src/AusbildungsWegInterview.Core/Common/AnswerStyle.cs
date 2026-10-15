namespace AusbildungsWegInterview.Core.Common;

/// <summary>Configurable tone of generated answer suggestions.</summary>
public enum AnswerStyle
{
    /// <summary>Short and direct.</summary>
    Concise = 0,

    /// <summary>Conversational, like a real interview answer. Default.</summary>
    Natural = 1,

    /// <summary>Formal, polished, structured.</summary>
    Professional = 2
}
