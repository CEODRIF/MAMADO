namespace AusbildungsWegInterview.Core.Audio;

/// <summary>
/// Energy-based silence gate to avoid spending CPU on quiet audio. The threshold
/// is a mean-amplitude value in [0,1]; raise it in noisy environments.
/// </summary>
public sealed class SilenceDetector
{
    private readonly double _rmsThreshold;

    public SilenceDetector(double rmsThreshold = 0.01)
    {
        if (rmsThreshold < 0 || rmsThreshold > 1)
            throw new ArgumentOutOfRangeException(nameof(rmsThreshold), "Threshold must be in [0,1].");
        _rmsThreshold = rmsThreshold;
    }

    public bool IsSilent(AudioFrame frame) => frame.MeanAmplitude() < _rmsThreshold;
}
