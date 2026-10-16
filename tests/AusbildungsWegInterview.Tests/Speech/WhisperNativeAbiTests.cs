using System.Runtime.InteropServices;
using AusbildungsWegInterview.Speech;
using Xunit;

namespace AusbildungsWegInterview.Tests.Speech;

/// <summary>
/// ABI regression guard for the pinned whisper.cpp (1.9.5) mirrors in
/// WhisperNative. Offsets are taken from the pinned include/whisper.h on x64
/// (C bool = 1 byte, size_t = 8 bytes, sequential + native alignment). If a
/// whisper.cpp update changes the ABI, these tests fail and the pin must be
/// re-audited before the mirrors change.
/// </summary>
public class WhisperNativeAbiTests
{
    [Fact]
    public void FullParams_MarshalSize_Is304_On64Bit()
    {
        Assert.True(Environment.Is64BitProcess, "This ABI guard is for x64.");
        Assert.Equal(304, Marshal.SizeOf<WhisperNative.WhisperFullParams>());
    }

    [Fact]
    public void ContextParams_MarshalSize_Is48_On64Bit()
    {
        Assert.True(Environment.Is64BitProcess, "This ABI guard is for x64.");
        Assert.Equal(48, Marshal.SizeOf<WhisperNative.WhisperContextParams>());
    }

    [Fact]
    public void VadParams_MarshalSize_Is24()
    {
        Assert.Equal(24, Marshal.SizeOf<WhisperNative.WhisperVadParams>());
    }

    [Fact]
    public void FullParams_KeyFieldOffsets_MatchPinnedHeader()
    {
        // Spot-checks across the whole struct (header + middle + tail), so any
        // field inserted/removed anywhere shifts a checked offset.
        Assert.Equal(8, Marshal.OffsetOf<WhisperNative.WhisperFullParams>("NMaxTextCtx"));
        Assert.Equal(32, Marshal.OffsetOf<WhisperNative.WhisperFullParams>("TholdPt"));
        Assert.Equal(64, Marshal.OffsetOf<WhisperNative.WhisperFullParams>("SuppressRegex"));
        Assert.Equal(104, Marshal.OffsetOf<WhisperNative.WhisperFullParams>("Language"));
        Assert.Equal(116, Marshal.OffsetOf<WhisperNative.WhisperFullParams>("Temperature"));
        Assert.Equal(144, Marshal.OffsetOf<WhisperNative.WhisperFullParams>("Greedy"));
        Assert.Equal(148, Marshal.OffsetOf<WhisperNative.WhisperFullParams>("BeamSearch"));
        Assert.Equal(240, Marshal.OffsetOf<WhisperNative.WhisperFullParams>("GrammarRules"));
        Assert.Equal(272, Marshal.OffsetOf<WhisperNative.WhisperFullParams>("VadModelPath"));
        Assert.Equal(280, Marshal.OffsetOf<WhisperNative.WhisperFullParams>("VadParams"));
    }

    [Fact]
    public void DefaultParams_MatchPinnedDefaults()
    {
        IntPtr lang = default;
        var p = WhisperNative.CreateFullParams(2, lang);
        Assert.Equal(0, p.Strategy);
        Assert.Equal(2, p.NThreads);
        Assert.Equal(16384, p.NMaxTextCtx);
        Assert.Equal(1, p.NoContext);
        Assert.Equal(0, p.PrintProgress);
        Assert.True(p.DetectLanguage == 0); // detection is a separate, explicit opt-in
        Assert.Equal(-1f, p.LengthPenalty);
        Assert.Equal(-1, p.Greedy.BestOf);
        Assert.Equal(-1, p.BeamSearch.BeamSize);
        Assert.True(p.Vad == 0);
        // The recognizer's detect path must set the flag and pass no language.
        var pDetect = WhisperNative.CreateFullParams(2, default, detectLanguage: true);
        Assert.True(pDetect.DetectLanguage == 1);
        Assert.Equal(default(IntPtr), pDetect.Language);
        // An explicit language pin is passed through untouched.
        var sentinel = new IntPtr(1);
        var p2 = WhisperNative.CreateFullParams(2, sentinel);
        Assert.True(p2.DetectLanguage == 0);
        Assert.Equal(sentinel, p2.Language);
    }

    [Fact]
    public void Resolver_CandidateOrder_IsOverrideThenAppDir()
    {
        string? previous = WhisperNative.LibraryOverridePath;
        try
        {
            string temp = Path.Combine(Path.GetTempPath(), $"aw-resolver-{Guid.NewGuid():N}");
            Directory.CreateDirectory(temp);
            string overrideFile = Path.Combine(temp, "libwhisper.so");
            File.WriteAllBytes(overrideFile, new byte[] { 0x00 });

            WhisperNative.LibraryOverridePath = overrideFile;
            var first = WhisperNative.Candidates("whisper").First();
            Assert.Equal(Path.GetFullPath(overrideFile), Path.GetFullPath(first));

            // Without an override, the app-relative library is the first candidate.
            WhisperNative.LibraryOverridePath = null;
            var appDir = WhisperNative.Candidates("whisper").First();
            Assert.Equal(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                OperatingSystem.IsWindows() ? "whisper.dll" : "libwhisper.so")),
                Path.GetFullPath(appDir));

            Directory.Delete(temp, true);
        }
        finally { WhisperNative.LibraryOverridePath = previous; }
    }
}
