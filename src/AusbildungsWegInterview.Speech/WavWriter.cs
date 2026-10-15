namespace AusbildungsWegInterview.Speech;

/// <summary>
/// Minimal 16-bit PCM mono WAV writer (canonical 44-byte header). Used to feed
/// whisper.cpp with the accumulated segment audio. No external dependencies.
/// </summary>
public static class WavWriter
{
    public static void Write16BitMono(string path, byte[] pcm16, int sampleRate)
    {
        if (pcm16 is null)
            throw new ArgumentNullException(nameof(pcm16));
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));

        int dataSize = pcm16.Length;
        int byteRate = sampleRate * 2;
        var header = new byte[44];

        WriteAscii(header, 0, "RIFF");
        WriteInt32LE(header, 4, 36 + dataSize);
        WriteAscii(header, 8, "WAVE");
        WriteAscii(header, 12, "fmt ");
        WriteInt32LE(header, 16, 16);      // fmt chunk size
        WriteInt16LE(header, 20, 1);       // audio format: PCM
        WriteInt16LE(header, 22, 1);       // channels: mono
        WriteInt32LE(header, 24, sampleRate);
        WriteInt32LE(header, 28, byteRate);
        WriteInt16LE(header, 32, 2);       // block align
        WriteInt16LE(header, 34, 16);      // bits per sample
        WriteAscii(header, 36, "data");
        WriteInt32LE(header, 40, dataSize);

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        fs.Write(header, 0, header.Length);
        fs.Write(pcm16, 0, dataSize);
    }

    private static void WriteAscii(byte[] b, int offset, string s)
    {
        for (int i = 0; i < s.Length && offset + i < b.Length; i++)
            b[offset + i] = (byte)s[i];
    }

    private static void WriteInt16LE(byte[] b, int offset, short value)
    {
        b[offset] = (byte)(value & 0xFF);
        b[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    private static void WriteInt32LE(byte[] b, int offset, int value)
    {
        b[offset] = (byte)(value & 0xFF);
        b[offset + 1] = (byte)((value >> 8) & 0xFF);
        b[offset + 2] = (byte)((value >> 16) & 0xFF);
        b[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
