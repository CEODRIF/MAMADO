using AusbildungsWegInterview.Core.Audio;
using Xunit;

namespace AusbildungsWegInterview.Tests.Core;

public class AudioChunkerTests
{
    [Fact]
    public void ProducesChunksAndKeepsRemainder()
    {
        // chunk = 0.5s @16k = 16000 bytes
        var chunker = new AudioChunker(16000, 0.5);
        var produced = chunker.Push(new AudioFrame(new byte[40000], 16000));
        Assert.Equal(2, produced.Count);
        Assert.Equal(16000, produced[0].Pcm.Length);

        var flush = chunker.Flush();
        Assert.NotNull(flush);
        Assert.Equal(8000, flush!.Pcm.Length);
    }

    [Fact]
    public void SmallInput_ProducesNoChunk_UntilEnough()
    {
        var chunker = new AudioChunker(16000, 0.5);
        var produced = chunker.Push(new AudioFrame(new byte[1000], 16000));
        Assert.Empty(produced);
        var flush = chunker.Flush();
        Assert.NotNull(flush);
        Assert.Equal(1000, flush!.Pcm.Length);
    }

    [Fact]
    public void Reset_DropsBufferedAudio()
    {
        var chunker = new AudioChunker(16000, 0.5);
        chunker.Push(new AudioFrame(new byte[8000], 16000));
        chunker.Reset();
        Assert.Null(chunker.Flush());
    }
}
