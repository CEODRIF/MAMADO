namespace AusbildungsWegInterview.Core.Audio;

/// <summary>
/// Accumulates mono 16-bit PCM and emits fixed-duration chunks suitable for the
/// speech recognizer. Keeps any remainder for the next push. Thread-affine: the
/// pipeline drives it from a single worker.
/// </summary>
public sealed class AudioChunker
{
    private readonly int _sampleRate;
    private readonly int _chunkBytes;
    private byte[] _buffer = Array.Empty<byte>();

    public AudioChunker(int sampleRate, double chunkSeconds)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (chunkSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkSeconds));

        _sampleRate = sampleRate;
        _chunkBytes = (int)Math.Round(sampleRate * chunkSeconds) * 2; // 2 bytes per 16-bit sample
    }

    /// <summary>Feeds mono 16-bit PCM and returns any complete chunks produced.</summary>
    public IReadOnlyList<AudioFrame> Push(AudioFrame frame)
    {
        var produced = new List<AudioFrame>();
        int total = _buffer.Length + frame.Pcm.Length;
        var next = new byte[total];
        Buffer.BlockCopy(_buffer, 0, next, 0, _buffer.Length);
        Buffer.BlockCopy(frame.Pcm, 0, next, _buffer.Length, frame.Pcm.Length);
        _buffer = next;

        int offset = 0;
        while (_buffer.Length - offset >= _chunkBytes)
        {
            var chunk = new byte[_chunkBytes];
            Buffer.BlockCopy(_buffer, offset, chunk, 0, _chunkBytes);
            produced.Add(new AudioFrame(chunk, _sampleRate, 16, 1));
            offset += _chunkBytes;
        }

        // Only drop consumed bytes; keep the remainder when no full chunk was produced yet.
        if (offset > 0)
            _buffer = Tail(_buffer, offset);
        return produced;
    }

    /// <summary>Flushes any remaining samples as a final (possibly short) chunk.</summary>
    public AudioFrame? Flush()
    {
        if (_buffer.Length < 2)
            return null;
        var remaining = _buffer;
        _buffer = Array.Empty<byte>();
        return new AudioFrame(remaining, _sampleRate, 16, 1);
    }

    /// <summary>Drops all buffered audio.</summary>
    public void Reset() => _buffer = Array.Empty<byte>();

    private static byte[] Tail(byte[] source, int offset)
    {
        int len = source.Length - offset;
        var tail = new byte[len];
        Buffer.BlockCopy(source, offset, tail, 0, len);
        return tail;
    }
}
