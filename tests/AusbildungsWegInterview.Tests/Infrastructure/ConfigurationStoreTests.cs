using AusbildungsWegInterview.Infrastructure.Config;
using Xunit;

namespace AusbildungsWegInterview.Tests.Infrastructure;

public class ConfigurationStoreTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void RoundTrip_PersistsChanges()
    {
        var path = TempPath();
        var store = new ConfigurationStore(path);
        try
        {
            var cfg = store.Load();
            cfg.AnswerLanguage = "fr";
            cfg.WhisperModel = "small";
            store.Save(cfg);

            var loaded = store.Load();
            Assert.Equal("fr", loaded.AnswerLanguage);
            Assert.Equal("small", loaded.WhisperModel);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MissingFile_ReturnsDefaults()
    {
        var store = new ConfigurationStore(TempPath());
        var cfg = store.Load();
        Assert.Equal("base", cfg.WhisperModel);
        Assert.Equal("de", cfg.AnswerLanguage);
    }

    [Fact]
    public void CorruptFile_ReturnsDefaults_WithoutThrowing()
    {
        var path = TempPath();
        File.WriteAllText(path, "{ this is not valid json");
        try
        {
            var store = new ConfigurationStore(path);
            var cfg = store.Load();
            Assert.Equal("base", cfg.WhisperModel);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Save_CreatesParentDirectory()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(tempRoot, "nested");
        var path = Path.Combine(dir, "config.json");
        var store = new ConfigurationStore(path);
        try
        {
            store.Save(AppConfigurationHolder.New());
            Assert.True(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    private static class AppConfigurationHolder
    {
        public static AusbildungsWegInterview.Core.Config.AppConfiguration New()
            => AusbildungsWegInterview.Core.Config.AppConfiguration.Default();
    }
}
