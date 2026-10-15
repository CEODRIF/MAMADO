using AusbildungsWegInterview.Infrastructure.Models;
using Xunit;

namespace AusbildungsWegInterview.Tests.Infrastructure;

public class WhisperModelManagerTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void Catalog_ContainsCommonModels()
    {
        var mgr = new WhisperModelManager(TempDir());
        Assert.Contains("base", mgr.AvailableModels);
        Assert.Contains("tiny", mgr.AvailableModels);
        Assert.Contains("small", mgr.AvailableModels);
    }

    [Fact]
    public void ResolvePath_UnknownModel_Throws()
    {
        var mgr = new WhisperModelManager(TempDir());
        Assert.Throws<ArgumentException>(() => mgr.ResolvePath("does-not-exist"));
    }

    [Fact]
    public void ResolvePath_KnownModel_UnderDirectory()
    {
        var dir = TempDir();
        var mgr = new WhisperModelManager(dir);
        Assert.StartsWith(dir, mgr.ResolvePath("base"));
        Assert.EndsWith("base.bin", mgr.ResolvePath("base"));
    }

    [Fact]
    public void IsPresent_False_WhenFileMissing()
    {
        var mgr = new WhisperModelManager(TempDir());
        Assert.False(mgr.IsPresent("base"));
    }

    [Fact]
    public void IsPresent_True_WhenFileExists()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "base.bin"), new byte[10]);
        var mgr = new WhisperModelManager(dir);
        Assert.True(mgr.IsPresent("base"));
        Directory.Delete(dir, true);
    }

    [Fact]
    public void GetInfo_Unknown_ReturnsNull()
    {
        var mgr = new WhisperModelManager(TempDir());
        Assert.Null(mgr.GetInfo("nope"));
    }
}
