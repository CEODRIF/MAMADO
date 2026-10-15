using AusbildungsWegInterview.Core.Audio;
using Xunit;

namespace AusbildungsWegInterview.Tests.Core;

public class SilenceDetectorTests
{
    [Fact]
    public void ClassifiesSilenceAndSound()
    {
        var detector = new SilenceDetector(0.01);
        var silent = new AudioFrame(new byte[4000], 16000);
        var loud = MakeLoud(4000);
        Assert.True(detector.IsSilent(silent));
        Assert.False(detector.IsSilent(loud));
    }

    [Fact]
    public void RejectsOutOfRangeThreshold()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new SilenceDetector(1.5));

    private static AudioFrame MakeLoud(int bytes)
    {
        var b = new byte[bytes];
        for (int i = 0; i < b.Length; i += 2) { b[i] = 0x00; b[i + 1] = 0x20; } // 8192
        return new AudioFrame(b, 16000);
    }
}
