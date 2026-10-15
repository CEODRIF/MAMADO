using AusbildungsWegInterview.Core.Audio;
using Xunit;

namespace AusbildungsWegInterview.Tests.Core;

public class PcmConverterTests
{
    [Fact]
    public void Resample_SameRate_ReturnsEqualData()
    {
        var data = new byte[] { 0x01, 0x00, 0x02, 0x00 };
        var result = PcmConverter.Resample(data, 16000, 16000);
        Assert.Equal(data, result);
    }

    [Fact]
    public void Resample_HalvesRate_HalvesSampleCount()
    {
        var data = MakeTone(100);
        var result = PcmConverter.Resample(data, 16000, 8000);
        Assert.Equal(50 * 2, result.Length);
    }

    [Fact]
    public void Downmix_StereoToMono_AveragesChannels()
    {
        // 2 stereo frames: (1000,1000) and (2000,2000)
        byte[] stereo = { 0xE8, 0x03, 0xE8, 0x03, 0xD0, 0x07, 0xD0, 0x07 };
        var mono = PcmConverter.DownmixToMono(stereo, 2);
        Assert.Equal(4, mono.Length);
        Assert.Equal(1000, Read(mono, 0));
        Assert.Equal(2000, Read(mono, 2));
    }

    [Fact]
    public void ToMono16k_ConvertsStereoFloatRateToMono16k()
    {
        // 4800 samples stereo 16-bit at 48000 -> mono 16-bit at 16000 (1600 samples)
        byte[] stereo = MakeStereo(4800, 2);
        var frame = new AudioFrame(stereo, 48000, 16, 2);
        var result = PcmConverter.ToMono16k(frame, 16000);
        Assert.Equal(1, result.Channels);
        Assert.Equal(16000, result.SampleRate);
        Assert.Equal(1600 * 2, result.Pcm.Length);
    }

    private static byte[] MakeTone(int samples)
    {
        var b = new byte[samples * 2];
        for (int i = 0; i < samples; i++)
        {
            short v = (short)(3000 * Math.Sin(i * 0.3));
            b[i * 2] = (byte)(v & 0xFF);
            b[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        return b;
    }

    private static byte[] MakeStereo(int samplesPerChannel, int channels)
    {
        var b = new byte[samplesPerChannel * channels * 2];
        for (int i = 0; i < b.Length; i += 2) { b[i] = 0x00; b[i + 1] = 0x10; } // ~4096
        return b;
    }

    private static short Read(byte[] mono16, int offset)
        => (short)(mono16[offset] | (mono16[offset + 1] << 8));
}
