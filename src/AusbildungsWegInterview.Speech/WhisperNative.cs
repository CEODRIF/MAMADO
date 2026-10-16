using System.Runtime.InteropServices;

namespace AusbildungsWegInterview.Speech;

/// <summary>
/// P/Invoke bindings for the whisper.cpp C API (libwhisper) — pinned.
///
/// PINNED RUNTIME: whisper.cpp commit <c>0c7be696371069e0baa2515ed7aa08d5a5473aa6</c>
/// (library version 1.9.5, ggml 0.26.0). Every struct below mirrors the pinned
/// <c>include/whisper.h</c> field-for-field (C <c>bool</c> = 1 byte, <c>size_t</c> =
/// 8 bytes on x64, sequential layout with native alignment). The size/offset
/// regression tests (WhisperNativeAbiTests) guard this mirror: if whisper.cpp
/// changes its ABI, the tests fail and the pin must be re-audited.
///
/// DLL resolution order: (1) <see cref="LibraryOverridePath"/> (tests / explicit
/// placement), (2) next to the application — where scripts/build-whisper.ps1
/// (.sh) place the pinned build, (3) the default system search. The ggml runtime
/// (ggml*.dll / libggml*.so) must sit BESIDE whisper.dll: the OS loader resolves
/// that dependency relative to the library itself.
///
/// If nothing resolves, the recognizer throws <see cref="WhisperRuntimeMissingException"/>
/// with an actionable message (no more silent empty transcripts).
/// </summary>
internal static class WhisperNative
{

    /// <summary>Pinned whisper.cpp major.minor (the ABI this mirror targets). The recognizer
    /// refuses to run a runtime whose <c>whisper_version()</c> differs at major.minor.</summary>
    public const string PinnedMajorMinor = "1.9";

    /// <summary>Full pinned library version (documentation + manifest).</summary>
    public const string PinnedVersion = "1.9.5";

    /// <summary>
    /// Explicit library file for tests / advanced placement. When set and the
    /// file exists, it wins over every other search location.
    /// </summary>
    internal static string? LibraryOverridePath;

    private const string LibName = "whisper";
    private const string GgmlName = "ggml";

    static WhisperNative()
        => NativeLibrary.SetDllImportResolver(typeof(WhisperNative).Assembly, Resolve);

    /// <summary>DllImport resolver: pinned locations first, then the default search.</summary>
    private static IntPtr Resolve(string name, System.Reflection.Assembly _, DllImportSearchPath? __)
    {
        if (!string.Equals(name, LibName, StringComparison.Ordinal) &&
            !string.Equals(name, GgmlName, StringComparison.Ordinal))
            return IntPtr.Zero;

        foreach (var candidate in Candidates(name))
        {
            try
            {
                if (NativeLibrary.TryLoad(candidate, out IntPtr h))
                    return h;
            }
            catch (Exception ex) when (ex is not DllNotFoundException)
            {
                // A corrupt/incompatible native image must not take down the
                // process — report it as a missing runtime (localized upstream).
            }
        }
        return IntPtr.Zero; // fall back to the OS default search order
    }

    /// <summary>
    /// Candidate locations in priority order (pure logic — unit-tested without
    /// loading anything).
    /// </summary>
    internal static IEnumerable<string> Candidates(string name)
    {
        if (!string.IsNullOrEmpty(LibraryOverridePath) && File.Exists(LibraryOverridePath))
            yield return LibraryOverridePath;

        string dir = AppContext.BaseDirectory;
        bool win = OperatingSystem.IsWindows();
        if (name == LibName)
        {
            if (win)
            {
                // MSVC builds name the DLL whisper.dll; MinGW-w64 builds
                // (scripts/build-whisper.ps1 default) name it libwhisper.dll.
                yield return Path.Combine(dir, "whisper.dll");
                yield return Path.Combine(dir, "libwhisper.dll");
            }
            else
                yield return Path.Combine(dir, "libwhisper.so");
        }
        else // ggml: versioned sonames vary — accept anything that matches.
        {
            foreach (var f in SafeEnumerate(dir, win ? "ggml.dll" : "libggml*.so"))
                yield return f;
        }
    }

    /// <summary>
    /// Preflight check used by the recognizer BEFORE any P/Invoke: returns the
    /// resolvable library path, or null when the runtime is genuinely absent.
    /// Also best-effort preloads the sibling ggml runtime (Linux safety net when
    /// the library carries no usable RPATH).
    /// </summary>
    internal static string? TryLocateLibrary()
    {
        foreach (var candidate in Candidates(LibName))
        {
            if (!TryLoad(candidate, out _))
                continue;
            string dir = Path.GetDirectoryName(candidate) ?? string.Empty;
            foreach (var sibling in SafeEnumerate(dir, OperatingSystem.IsWindows()
                ? "ggml*.dll"
                : "libggml*.so"))
                TryLoad(sibling, out _);
            return candidate;
        }
        return null;
    }

    private static bool TryLoad(string path, out IntPtr handle)
    {
        handle = IntPtr.Zero;
        try { return NativeLibrary.TryLoad(path, out handle); }
        catch (Exception ex) when (ex is not DllNotFoundException) { return false; }
    }

    private static IEnumerable<string> SafeEnumerate(string dir, string pattern)
    {
        try { return Directory.EnumerateFiles(dir, pattern).OrderBy(p => p).ToList(); }
        catch { return Enumerable.Empty<string>(); }
    }

    // ---- struct mirrors of the pinned include/whisper.h (see class header) ----

    [StructLayout(LayoutKind.Sequential)]
    public struct WhisperVadParams
    {
        public float Threshold;
        public int MinSpeechDurationMs;
        public int MinSilenceDurationMs;
        public float MaxSpeechDurationS;
        public int SpeechPadMs;
        public float SamplesOverlap;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WhisperAheads
    {
        public UIntPtr NHeads;
        public IntPtr Heads;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WhisperContextParams
    {
        public byte UseGpu;
        public byte FlashAttn;
        public int GpuDevice;
        public byte DtwTokenTimestamps;
        public int DtwAheadsPreset;
        public int DtwNTop;
        public WhisperAheads DtwAheads;
        public UIntPtr DtwMemSize;
    }

    /// <summary>CPU-only context (this app is local/CPU by design; no GPU path).</summary>
    internal static WhisperContextParams CreateContextParams() => new()
    {
        UseGpu = 0,
        FlashAttn = 0,
        GpuDevice = 0,
        DtwTokenTimestamps = 0,
        DtwAheadsPreset = 0,
        DtwNTop = 0,
        DtwAheads = new WhisperAheads { NHeads = UIntPtr.Zero, Heads = IntPtr.Zero },
        DtwMemSize = UIntPtr.Zero
    };

    [StructLayout(LayoutKind.Sequential)]
    public struct WhisperGreedy { public int BestOf; }

    [StructLayout(LayoutKind.Sequential)]
    public struct WhisperBeamSearch { public int BeamSize; public float Patience; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct WhisperFullParams
    {
        public int Strategy;               // WHISPER_SAMPLING_GREEDY = 0
        public int NThreads;
        public int NMaxTextCtx;
        public int OffsetMs;
        public int DurationMs;
        public byte Translate;
        public byte NoContext;
        public byte NoTimestamps;
        public byte SingleSegment;
        public byte PrintSpecial;
        public byte PrintProgress;
        public byte PrintRealtime;
        public byte PrintTimestamps;
        public byte TokenTimestamps;
        public float TholdPt;
        public float TholdPtSum;
        public int MaxLen;
        public byte SplitOnWord;
        public int MaxTokens;
        public byte DebugMode;
        public int AudioCtx;
        public byte TdrzEnable;
        public IntPtr SuppressRegex;
        public IntPtr InitialPrompt;
        public byte CarryInitialPrompt;
        public IntPtr PromptTokens;
        public int PromptNTokens;
        public IntPtr Language;            // const char* (UTF-8; our codes are ASCII)
        public byte DetectLanguage;
        public byte SuppressBlank;
        public byte SuppressNst;
        public float Temperature;
        public float MaxInitialTs;
        public float LengthPenalty;
        public float TemperatureInc;
        public float EntropyThold;
        public float LogprobThold;
        public float NoSpeechThold;
        public WhisperGreedy Greedy;
        public WhisperBeamSearch BeamSearch;
        public IntPtr NewSegmentCallback;
        public IntPtr NewSegmentCallbackUserData;
        public IntPtr ProgressCallback;
        public IntPtr ProgressCallbackUserData;
        public IntPtr EncoderBeginCallback;
        public IntPtr EncoderBeginCallbackUserData;
        public IntPtr AbortCallback;
        public IntPtr AbortCallbackUserData;
        public IntPtr LogitsFilterCallback;
        public IntPtr LogitsFilterCallbackUserData;
        public IntPtr GrammarRules;
        public UIntPtr NGrammarRules;
        public UIntPtr IStartRule;
        public float GrammarPenalty;
        public byte Vad;
        public IntPtr VadModelPath;
        public WhisperVadParams VadParams;
    }

    /// <summary>
    /// Canonical values for the pinned version — mirrors whisper.cpp's own
    /// whisper_full_default_params(GREEDY), with: quiet prints (we render our
    /// own UI) and the configured thread count.
    ///
    /// Language semantics (pinned header: "for auto-detection, set to
    /// nullptr, """" or ""auto"""): detect_language = 1 makes whisper_full
    /// RETURN RIGHT AFTER detection (rc 0, no transcription — do NOT use it).
    /// So detection is always done via language = "auto" + detect_language = 0,
    /// which detects AND continues transcribing in the same call; an explicit
    /// language pins detection off.
    /// </summary>
    internal static WhisperFullParams CreateFullParams(int nThreads, IntPtr language, bool detectLanguage = false) => new()
    {
        Strategy = 0, // WHISPER_SAMPLING_GREEDY
        NThreads = Math.Max(1, nThreads),
        NMaxTextCtx = 16384,
        OffsetMs = 0,
        DurationMs = 0,
        Translate = 0,
        NoContext = 1,
        NoTimestamps = 0,
        SingleSegment = 0,
        PrintSpecial = 0,
        PrintProgress = 0,
        PrintRealtime = 0,
        PrintTimestamps = 0,
        TokenTimestamps = 0,
        TholdPt = 0.01f,
        TholdPtSum = 0.01f,
        MaxLen = 0,
        SplitOnWord = 0,
        MaxTokens = 0,
        DebugMode = 0,
        AudioCtx = 0,
        TdrzEnable = 0,
        SuppressRegex = IntPtr.Zero,
        InitialPrompt = IntPtr.Zero,
        CarryInitialPrompt = 0,
        PromptTokens = IntPtr.Zero,
        PromptNTokens = 0,
        Language = language,
        DetectLanguage = detectLanguage ? (byte)1 : (byte)0,
        SuppressBlank = 1,
        SuppressNst = 0,
        Temperature = 0.0f,
        MaxInitialTs = 1.0f,
        LengthPenalty = -1.0f,
        TemperatureInc = 0.2f,
        EntropyThold = 2.4f,
        LogprobThold = -1.0f,
        NoSpeechThold = 0.6f,
        Greedy = new WhisperGreedy { BestOf = -1 },
        BeamSearch = new WhisperBeamSearch { BeamSize = -1, Patience = -1.0f },
        NewSegmentCallback = IntPtr.Zero,
        NewSegmentCallbackUserData = IntPtr.Zero,
        ProgressCallback = IntPtr.Zero,
        ProgressCallbackUserData = IntPtr.Zero,
        EncoderBeginCallback = IntPtr.Zero,
        EncoderBeginCallbackUserData = IntPtr.Zero,
        AbortCallback = IntPtr.Zero,
        AbortCallbackUserData = IntPtr.Zero,
        LogitsFilterCallback = IntPtr.Zero,
        LogitsFilterCallbackUserData = IntPtr.Zero,
        GrammarRules = IntPtr.Zero,
        NGrammarRules = UIntPtr.Zero,
        IStartRule = UIntPtr.Zero,
        GrammarPenalty = 0.0f,
        Vad = 0,
        VadModelPath = IntPtr.Zero,
        VadParams = new WhisperVadParams()
    };

    // ---- exports (pinned 1.9.5 ABI; note: whisper_full takes params BY VALUE) ----

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "whisper_version")]
    private static extern IntPtr whisper_version();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "whisper_init_from_file_with_params")]
    public static extern IntPtr whisper_init_from_file_with_params(string modelPath, WhisperContextParams parameters);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "whisper_free")]
    public static extern void whisper_free(IntPtr context);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "whisper_full")]
    public static extern int whisper_full(IntPtr context, WhisperFullParams parameters, float[] samples, int nSamples);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "whisper_full_n_segments")]
    public static extern int whisper_full_n_segments(IntPtr context);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "whisper_full_get_segment_text")]
    public static extern IntPtr whisper_full_get_segment_text(IntPtr context, int iSegment);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "whisper_full_lang_id")]
    public static extern int whisper_full_lang_id(IntPtr context);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "whisper_lang_str")]
    public static extern IntPtr whisper_lang_str(int languageId);

    /// <summary>Runtime version string (e.g. "1.9.5"); "" when the library could not be loaded.</summary>
    public static string Version()
    {
        IntPtr p = whisper_version();
        return p == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(p) ?? string.Empty;
    }

    public static string PtrToString(IntPtr p)
        => p == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(p) ?? string.Empty;
}
