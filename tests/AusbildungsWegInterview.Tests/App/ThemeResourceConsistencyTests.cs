using System.Text.RegularExpressions;
using Xunit;

namespace AusbildungsWegInterview.Tests.App;

/// <summary>
/// WPF-free theme/resource consistency audit (the WPF runtime itself is only
/// verifiable on Windows): the Light and Dark dictionaries must define the
/// SAME resource keys, and every DynamicResource/StaticResource referenced in
/// the app's XAML must be defined somewhere (themes, App.xaml, window
/// resources). Catches a missing brush in one theme (the classic "works in
/// light, breaks in dark" bug) before Windows verification.
/// </summary>
public class ThemeResourceConsistencyTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("AusbildungsWegInterview.sln").Any())
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repository root (AusbildungsWegInterview.sln) not found above the test output directory.");
    }

    private static string AppDir() => Path.Combine(FindRepoRoot(), "src", "AusbildungsWegInterview.App");

    private static string[] AppXamlFiles()
        => Directory.EnumerateFiles(AppDir(), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains("obj", StringComparison.OrdinalIgnoreCase) && !f.Contains("bin", StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private static string StripComments(string xaml)
        // XAML comments can contain "StaticResource …" in prose (App.xaml); ignore them.
        => Regex.Replace(xaml, "<!--.*?-->", " ", RegexOptions.Singleline);

    private static HashSet<string> DefinedKeys(string xaml)
        => new(Regex.Matches(StripComments(xaml), "x:Key=\"([^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value));

    private static HashSet<string> ReferencedKeys(string xaml)
        => new(Regex.Matches(StripComments(xaml), @"(?:DynamicResource|StaticResource)\s+([A-Za-z0-9_]+)")
            .Cast<Match>().Select(m => m.Groups[1].Value));

    [Fact]
    public void LightAndDark_DefineExactlyTheSameResourceKeys()
    {
        var light = DefinedKeys(File.ReadAllText(Path.Combine(AppDir(), "Themes", "AwTheme.Light.xaml")));
        var dark = DefinedKeys(File.ReadAllText(Path.Combine(AppDir(), "Themes", "AwTheme.Dark.xaml")));

        Assert.True(light.Count > 0, "Light theme defines no keyed resources (path wrong?).");
        var lightOnly = light.Except(dark).ToArray();
        var darkOnly = dark.Except(light).ToArray();
        Assert.Empty(lightOnly);
        Assert.Empty(darkOnly);
    }

    [Fact]
    public void EveryReferencedResource_IsDefined()
    {
        var files = AppXamlFiles();
        Assert.True(files.Length >= 4, $"Expected at least 4 XAML files in the App project, found {files.Length}.");

        var defined = new HashSet<string>();
        var referenced = new HashSet<string>();
        foreach (var f in files)
        {
            string xaml = File.ReadAllText(f);
            defined.UnionWith(DefinedKeys(xaml));
            referenced.UnionWith(ReferencedKeys(xaml));
        }

        var missing = referenced.Except(defined).ToArray();
        Assert.Empty(missing);
    }
}
