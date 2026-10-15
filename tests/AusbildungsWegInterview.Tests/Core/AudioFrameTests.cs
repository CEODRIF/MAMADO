using AusbildungsWegInterview.Core.Audio;
using Xunit;

namespace AusbildungsWegInterview.Tests.Core;

public class AudioFrameTests
{
    [Fact]
    public void ComputesSampleCountAndDuration()
    {
        var frame = new AudioFrame(new byte[32000], 16000, 16, 1);
        Assert.Equal(16000, frame.SampleCount);
        Assert.Equal(1000, (int)frame.DurationMs);
    }

    [Fact]
    public void RejectsEmptyPcm()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new AudioFrame(new byte[0], 16000));

    [Fact]
    public void RejectsNonPositiveRate()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new AudioFrame(new byte[100], 0));

    [Fact]
    public void RejectsZeroChannels()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new AudioFrame(new byte[100], 16000, 16, 0));

    [Fact]
    public void MeanAmplitude_ZeroBuffer_IsZero()
    {
        var frame = new AudioFrame(new byte[1000], 16000);
        Assert.Equal(0, frame.MeanAmplitude());
    }

    [Fact]
    public void MeanAmplitude_FullScale_IsAboutOne()
    {
        var b = new byte[200];
        for (int i = 0; i < b.Length; i += 2) { b[i] = 0xFF; b[i + 1] = 0x7F; } // 32767
        var frame = new AudioFrame(b, 16000);
        Assert.True(frame.MeanAmplitude() > 0.99);
    }
}
