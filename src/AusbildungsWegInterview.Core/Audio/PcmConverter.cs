namespace AusbildungsWegInterview.Core.Audio;

/// <summary>
/// Pure PCM helpers (16-bit) used to normalize captured audio to the format the
/// speech recognizer expects (mono, target sample rate). No native dependencies,
/// fully unit-testable on any OS.
/// </summary>
public static class PcmConverter
{
    /// <summary>Converts a captured frame to mono 16-bit PCM at <paramref name="targetRate"/>.</summary>
    public static AudioFrame ToMono16k(AudioFrame frame, int targetRate)
    {
        if (frame.BitsPerSample != 16)
            throw new NotSupportedException($"Only 16-bit PCM is supported, got {frame.BitsPerSample}-bit.");

        byte[] mono = frame.Channels > 1 ? DownmixToMono(frame.Pcm, frame.Channels) : frame.Pcm;
        byte[] resampled = Resample(mono, frame.SampleRate, targetRate);
        return new AudioFrame(resampled, targetRate, 16, 1);
    }

    /// <summary>Averages the channels of 16-bit interleaved PCM down to mono.</summary>
    public static byte[] DownmixToMono(byte[] pcm16, int channels)
    {
        if (channels < 1)
            throw new ArgumentOutOfRangeException(nameof(channels));

        int samplesPerFrame = pcm16.Length / 2 / channels;
        var mono = new byte[samplesPerFrame * 2];
        for (int f = 0; f < samplesPerFrame; f++)
        {
            long sum = 0;
            for (int c = 0; c < channels; c++)
            {
                int idx = (f * channels + c) * 2;
                short s = (short)(pcm16[idx] | (pcm16[idx + 1] << 8));
                sum += s;
            }
            short avg = (short)(sum / channels);
            mono[f * 2] = (byte)(avg & 0xFF);
            mono[f * 2 + 1] = (byte)((avg >> 8) & 0xFF);
        }
        return mono;
    }

    /// <summary>
    /// Linear-interpolation resampler for 16-bit mono PCM. Adequate for speech;
    /// a higher-quality filter can be swapped in without changing the pipeline.
    /// </summary>
    public static byte[] Resample(byte[] mono16, int fromRate, int toRate)
    {
        if (fromRate <= 0 || toRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(toRate), "Rates must be positive.");
        if (fromRate == toRate)
            return (byte[])mono16.Clone();

        int sampleCount = mono16.Length / 2;
        if (sampleCount == 0)
            return mono16;

        double ratio = fromRate / (double)toRate;
        int outCount = Math.Max(1, (int)Math.Round(sampleCount / ratio));
        var outSamples = new short[outCount];

        for (int i = 0; i < outCount; i++)
        {
            double pos = i * ratio;
            int idx = (int)pos;
            double frac = pos - idx;
            int a = idx < sampleCount ? ReadSample(mono16, idx) : ReadSample(mono16, sampleCount - 1);
            int b = (idx + 1) < sampleCount ? ReadSample(mono16, idx + 1) : a;
            int val = (int)Math.Round(a + frac * (b - a));
            outSamples[i] = (short)Math.Clamp(val, short.MinValue, short.MaxValue);
        }

        var result = new byte[outCount * 2];
        for (int i = 0; i < outCount; i++)
        {
            result[i * 2] = (byte)(outSamples[i] & 0xFF);
            result[i * 2 + 1] = (byte)((outSamples[i] >> 8) & 0xFF);
        }
        return result;
    }

    private static int ReadSample(byte[] mono16, int index)
        => (short)(mono16[index * 2] | (mono16[index * 2 + 1] << 8));
}
