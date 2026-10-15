namespace AusbildungsWegInterview.Infrastructure;

/// <summary>
/// Best-effort, cross-platform probe of local hardware so the app can make
/// conservative model recommendations without assuming a GPU or fixed RAM.
/// Values are approximate; they only inform suggestions, never hard requirements.
/// </summary>
public static class SystemInfo
{
    public static int ProcessorCount => Environment.ProcessorCount;

    /// <summary>Approximate physical memory available to the process (bytes); 0 if unknown.</summary>
    public static long PhysicalMemoryBytes
    {
        get
        {
            try { return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes; }
            catch { return 0; }
        }
    }

    public static double PhysicalMemoryGB => PhysicalMemoryBytes / 1024.0 / 1024.0 / 1024.0;

    /// <summary>
    /// Suggests a conservative Ollama model for the available RAM (local, free models).
    /// These are all free/local; nothing is downloaded automatically.
    /// </summary>
    public static string RecommendOllamaModel()
    {
        double gb = PhysicalMemoryGB;
        if (gb <= 0) return "llama3.2:3b"; // unknown -> a safe small default
        if (gb >= 16) return "llama3.1:8b";
        if (gb >= 8) return "llama3.2:3b";
        return "phi3:mini";
    }

    /// <summary>Suggests a whisper.cpp speech model for the available RAM.</summary>
    public static string RecommendWhisperModel()
    {
        double gb = PhysicalMemoryGB;
        if (gb <= 0) return "base";
        if (gb >= 16) return "small";
        if (gb >= 8) return "base";
        return "tiny";
    }
}
