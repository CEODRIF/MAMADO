using AusbildungsWegInterview.Speech;
using Xunit;

namespace AusbildungsWegInterview.Tests.Speech;

/// <summary>
/// The first-run runtime probe: the pinned-version classification is pure and
/// fully testable; the "library missing" probe path is deterministic via the
/// internal override (points at a path that does not exist).
/// </summary>
public class WhisperRuntimeProbeTests
{
    [Theory]
    [InlineData("1.9.5", true)]
    [InlineData("1.9.5-dev", true)]   // non-tagged checkout suffix
    [InlineData("1.9", true)]
    [InlineData("1.10.0", false)]    // newer major.minor: different ABI
    [InlineData("1.5.0", false)]     // older: the original silent-failure ABI
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsPinnedVersion_ClassifiesAgainstThePinnedMajorMinor(string? version, bool expected)
    {
        Assert.Equal(expected, WhisperRuntimeProbe.IsPinnedVersion(version));
    }

    [Fact]
    public void Probe_NoLibraryLocatable_IsMissing()
    {
        string? previous = WhisperNative.LibraryOverridePath;
        try
        {
            // A path that cannot exist: the resolver must yield "missing",
            // never throw — the probe runs on every startup.
            WhisperNative.LibraryOverridePath = Path.Combine(Path.GetTempPath(), "definitely-not-here-aw", "whisper.dll");

            var result = WhisperRuntimeProbe.Probe();

            Assert.Equal(WhisperRuntimeState.Missing, result.State);
            Assert.Null(result.FoundVersion);
        }
        finally
        {
            WhisperNative.LibraryOverridePath = previous;
        }
    }

    [Fact]
    public void Probe_IsNeverThrowing()
    {
        // Whatever the host provides (or not), Probe() must return a result.
        var result = WhisperRuntimeProbe.Probe();
        Assert.True(
            result.State is WhisperRuntimeState.Missing or WhisperRuntimeState.Ready or WhisperRuntimeState.VersionMismatch,
            "probe returned an invalid state: " + result.State);
    }
}
