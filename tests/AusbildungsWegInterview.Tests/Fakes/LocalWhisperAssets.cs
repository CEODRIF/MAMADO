namespace AusbildungsWegInterview.Tests.Fakes;

/// <summary>
/// Shared locator for a locally built whisper.cpp (real-binary integration tests).
/// Resolves from the env override or the repository's sibling tools directory.
/// Returns null when unavailable so callers can skip gracefully.
/// </summary>
internal static class LocalWhisperAssets
{
    public sealed record Loc(string Bin, string Model, string JfkWav);

    /// <summary>Native shared library + model + fixture (for P/Invoke smoke tests).</summary>
    public sealed record LocNative(string Lib, string Model, string JfkWav);

    public static Loc? Find()
    {
        string? bin = null;
        string? envBin = Environment.GetEnvironmentVariable("AUSBILDUNGSWEG_WHISPER_CLI");
        if (!string.IsNullOrWhiteSpace(envBin) && File.Exists(envBin))
            bin = Path.GetFullPath(envBin);

        if (bin is null)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (string.Equals(dir.Name, "MAMADO", StringComparison.OrdinalIgnoreCase))
                {
                    string candidate = Path.GetFullPath(Path.Combine(
                        dir.FullName, "..", "tools", "whisper.cpp", "build", "bin", "whisper-cli"));
                    if (File.Exists(candidate))
                        bin = candidate;
                    break;
                }
                dir = dir.Parent;
            }
        }
        if (bin is null)
            return null;

        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(bin)!, "..", ".."));
        string model = Path.Combine(root, "models", "ggml-tiny.bin");
        string jfk = Path.Combine(root, "samples", "jfk.wav");
        return File.Exists(model) && File.Exists(jfk) ? new Loc(bin, model, jfk) : null;
    }

    /// <summary>
    /// Locates a locally built whisper SHARED library (env override, then the
    /// repository's sibling tools build output). Null when unavailable.
    /// </summary>
    public static LocNative? FindNative()
    {
        string? lib = null;
        string? envLib = Environment.GetEnvironmentVariable("AUSBILDUNGSWEG_WHISPER_LIB");
        if (!string.IsNullOrWhiteSpace(envLib) && File.Exists(envLib))
            lib = Path.GetFullPath(envLib);

        if (lib is null)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (string.Equals(dir.Name, "MAMADO", StringComparison.OrdinalIgnoreCase))
                {
                    string binDir = Path.Combine(dir.FullName, "..", "tools", "whisper.cpp", "build", "bin");
                    if (Directory.Exists(binDir))
                    {
                        bool win = OperatingSystem.IsWindows();
                        var match = Directory.EnumerateFiles(binDir, win ? "whisper.dll" : "libwhisper.so*")
                            .FirstOrDefault(f => !f.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase));
                        if (match is not null)
                            lib = Path.GetFullPath(match);
                    }
                    break;
                }
                dir = dir.Parent;
            }
        }
        if (lib is null)
            return null;

        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(lib)!, "..", ".."));
        string? envModel = Environment.GetEnvironmentVariable("AUSBILDUNGSWEG_WHISPER_MODEL");
        string model = (!string.IsNullOrWhiteSpace(envModel) && File.Exists(envModel))
            ? Path.GetFullPath(envModel)
            : Path.Combine(root, "models", "ggml-tiny.bin");
        string jfk = Path.Combine(root, "samples", "jfk.wav");
        return File.Exists(model) && File.Exists(jfk) ? new LocNative(lib, model, jfk) : null;
    }

    /// <summary>Reads the PCM payload of a 16-bit mono WAV (robust to extra RIFF chunks).</summary>
    public static byte[] ReadPcm16MonoWav(string path, out int sampleRate)
    {
        var bytes = File.ReadAllBytes(path);
        sampleRate = 0;
        int dataOffset = -1, dataLen = 0;
        int pos = 12;
        while (pos + 8 <= bytes.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
            int size = BitConverter.ToInt32(bytes, pos + 4);
            if (id == "fmt ")
                sampleRate = BitConverter.ToInt32(bytes, pos + 12);
            if (id == "data")
            {
                dataOffset = pos + 8;
                dataLen = size;
                break;
            }
            pos += 8 + size + (size & 1);
        }
        if (dataOffset <= 0)
            throw new InvalidDataException("WAV has no data chunk: " + path);
        var pcm = new byte[dataLen];
        Buffer.BlockCopy(bytes, dataOffset, pcm, 0, dataLen);
        return pcm;
    }
}
