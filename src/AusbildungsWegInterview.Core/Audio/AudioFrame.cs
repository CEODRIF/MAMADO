namespace AusbildungsWegInterview.Core.Audio;

/// <summary>
/// An immutable chunk of raw PCM audio captured from the system output.
/// The capture layer normalizes to 16-bit PCM; sample rate and channel count
/// are carried so downstream stages can resample/downmix as needed.
/// </summary>
public sealed record AudioFrame
{
    public byte[] Pcm { get; }
    public int SampleRate { get; }
    public int BitsPerSample { get; }
    public int Channels { get; }

    public AudioFrame(byte[] pcm, int sampleRate, int bitsPerSample = 16, int channels = 1)
    {
        if (pcm is null || pcm.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(pcm), "Audio frame must contain PCM data.");
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be positive.");
        if (channels < 1)
            throw new ArgumentOutOfRangeException(nameof(channels), "Channels must be at least 1.");

        Pcm = pcm;
        SampleRate = sampleRate;
        BitsPerSample = bitsPerSample;
        Channels = channels;
    }

    /// <summary>Length of this chunk in whole samples.</summary>
    public int SampleCount
    {
        get
        {
            int bytesPerSample = (BitsPerSample / 8) * Channels;
            return bytesPerSample > 0 ? Pcm.Length / bytesPerSample : 0;
        }
    }

    /// <summary>Approximate duration of this chunk in milliseconds.</summary>
    public double DurationMs => SampleRate > 0 ? (SampleCount * 1000.0) / SampleRate : 0;

    /// <summary>Mean absolute amplitude (0..1) used by simple silence detection.</summary>
    public double MeanAmplitude()
    {
        // Only supports 16-bit PCM, which is the format we capture in.
        if (BitsPerSample != 16)
            return 0;

        double sum = 0;
        int count = 0;
        for (int i = 0; i + 1 < Pcm.Length; i += 2)
        {
            short sample = (short)(Pcm[i] | (Pcm[i + 1] << 8));
            sum += Math.Abs(sample);
            count++;
        }
        return count == 0 ? 0 : sum / (count * 32768.0);
    }
}
