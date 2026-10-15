namespace AusbildungsWegInterview.Core.Common;

/// <summary>
/// A performance/quality preset that maps to concrete local model choices.
/// The app must not assume a dedicated GPU is present.
/// </summary>
public enum QualityProfile
{
    /// <summary>Smallest speech model, smallest quantized LLM. Lowest RAM/VRAM.</summary>
    Fast = 0,

    /// <summary>Default balance of quality and resource use.</summary>
    Balanced = 1,

    /// <summary>Larger speech model and LLM for maximum quality.</summary>
    High = 2
}
