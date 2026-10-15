using System.Runtime.InteropServices;

namespace AusbildungsWegInterview.Speech;

/// <summary>
/// P/Invoke bindings for the whisper.cpp C API (libwhisper).
///
/// The native library is NOT bundled and is resolved at runtime (whisper.dll on
/// Windows, libwhisper.so on Linux). If it is absent, the first call throws
/// DllNotFoundException, which the recognizer treats as "runtime unavailable".
///
/// Target: whisper.cpp (https://github.com/ggml-org/whisper.cpp). The
/// <see cref="whisper_full_params"/> layout follows the documented C API; if a
/// specific whisper.cpp build differs, the native path is only affected (this
/// project still compiles and degrades gracefully). Live transcription is
/// verified against a pinned build in the transcription milestone (Step 5).
/// </summary>
internal static class WhisperNative
{
    private const string Lib = "whisper";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct whisper_full_params
    {
        public int strategy;               // WHISPER_STRATEGY (0 = local)
        public int n_threads;
        public int translate;              // 0/1
        public int detect_language;        // 0/1
        public int print_progress;
        public int print_realtime;
        public int print_timestamps;
        public int print_special_tokens;
        public int print_commands;
        public IntPtr token_callback;      // function pointer (null = none)
        public int verbosity;              // WHISPER_LOG_LEVEL (0 = error)
        public float temperature;
        public float tbs_min_len;
        public float tbs_max_len;
        public int max_tokens;             // 0 = WHISPER_TOKEN_MAIN
        public IntPtr full_callback;       // function pointer (null = none)
        public int suppress_blank;
        public int suppress_non_speech_tokens;
        public float temperature_inc;
        public float max_initial_ts;
        public float length_penalty;
        [MarshalAs(UnmanagedType.LPStr)] public string? initial_prompt;
        public int grammar;
        public int n_grammar_rules;
    }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "whisper_init_from_file_v2")]
    public static extern IntPtr whisper_init_from_file_v2(string modelPath, int nThreads);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "whisper_free")]
    public static extern void whisper_free(IntPtr ctx);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "whisper_full")]
    public static extern int whisper_full(IntPtr ctx, ref whisper_full_params @params, float[] pcm, int nSamples);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "whisper_full_n_segments")]
    public static extern int whisper_full_n_segments(IntPtr ctx);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "whisper_full_get_segment_text")]
    public static extern IntPtr whisper_full_get_segment_text(IntPtr ctx, int segmentIndex);

    public static string PtrToString(IntPtr p)
        => p == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(p) ?? string.Empty;

    public static whisper_full_params CreateDefaultParams(int nThreads) => new()
    {
        strategy = 0,
        n_threads = Math.Max(1, nThreads),
        translate = 0,
        detect_language = 1,
        print_progress = 0,
        print_realtime = 0,
        print_timestamps = 0,
        print_special_tokens = 0,
        print_commands = 0,
        token_callback = IntPtr.Zero,
        verbosity = 0,
        temperature = 0.0f,
        tbs_min_len = 0.0f,
        tbs_max_len = 0.0f,
        max_tokens = 0,
        full_callback = IntPtr.Zero,
        suppress_blank = 1,
        suppress_non_speech_tokens = 1,
        temperature_inc = 0.2f,
        max_initial_ts = 10.0f,
        length_penalty = 0.0f,
        initial_prompt = null,
        grammar = 0,
        n_grammar_rules = 0
    };
}
