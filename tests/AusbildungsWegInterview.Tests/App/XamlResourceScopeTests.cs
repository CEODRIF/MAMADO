using System.Text.RegularExpressions;
using Xunit;

namespace AusbildungsWegInterview.Tests.App;

/// <summary>
/// Regression tests for WPF resource-scoping crashes that only surface at
/// runtime (BAML compilation does NOT resolve StaticResource references).
///
/// History: MainWindow's start-tag attributes
/// (Title="{Binding ..., Converter={StaticResource LocKey}}" and
/// FlowDirection="{StaticResource BoolToFlowDirection}") referenced converters
/// declared inside &lt;Window.Resources&gt; - a child element the XAML parser loads
/// AFTER the start tag, so InitializeComponent crashed on Windows with
/// "Cannot find resource named 'LocKey'". Converters now live in App.xaml.
///
/// These tests parse the XAML as text (no WPF runtime needed) and verify the
/// scoping rules WPF enforces at load time.
/// </summary>
public class XamlResourceScopeTests
{
    private static string RepoRoot()
    {
        // <repo>/tests/AusbildungsWegInterview.Tests/bin/{Config}/{TFM}/ -> 5 levels up
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    private static string Read(string relPath)
        => File.ReadAllText(Path.Combine(RepoRoot(), relPath.Replace('/', Path.DirectorySeparatorChar)));

    private static HashSet<string> DeclaredKeys(string xaml)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(xaml, @"x:Key=""([^""]+)"""))
            keys.Add(m.Groups[1].Value);
        return keys;
    }

    private static HashSet<string> StaticResourceRefsIn(string xaml)
    {
        var refs = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(xaml, @"\{StaticResource\s+([A-Za-z0-9_]+)"))
            refs.Add(m.Groups[1].Value);
        return refs;
    }

    private static HashSet<string> DynamicResourceRefsIn(string xaml)
    {
        var refs = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(xaml, @"\{DynamicResource\s+([A-Za-z0-9_]+)"))
            refs.Add(m.Groups[1].Value);
        return refs;
    }

    private static string WindowStartTag(string xaml)
    {
        int start = xaml.IndexOf("<Window", StringComparison.Ordinal);
        Assert.True(start >= 0, "MainWindow.xaml must contain a <Window element");
        int end = xaml.IndexOf('>', start);
        Assert.True(end > start, "unterminated window start tag");
        return xaml[start..(end + 1)];
    }

    /// <summary>THE regression: every StaticResource used in window START-TAG attributes must be declared in App.xaml.</summary>
    [Fact]
    public void WindowStartTag_Resources_AreDeclaredInApplicationScope()
    {
        var window = Read("src/AusbildungsWegInterview.App/MainWindow.xaml");
        var app = Read("src/AusbildungsWegInterview.App/App.xaml");

        var startTagRefs = StaticResourceRefsIn(WindowStartTag(window));
        Assert.NotEmpty(startTagRefs); // sanity: Title/FlowDirection do use converters

        var appKeys = DeclaredKeys(app);
        foreach (var key in startTagRefs)
            Assert.True(appKeys.Contains(key),
                $"window start-tag attribute references StaticResource '{key}', which is not declared in App.xaml (parsed too late in Window.Resources)");
    }

    [Fact]
    public void ApplicationScope_DeclaresTheLocalizationAndRtlConverters()
    {
        var app = Read("src/AusbildungsWegInterview.App/App.xaml");
        var keys = DeclaredKeys(app);
        Assert.Contains("LocKey", keys);
        Assert.Contains("BoolToFlowDirection", keys);
        Assert.Contains("BoolToVis", keys);
        Assert.Contains("InverseBoolToVis", keys);
        Assert.Contains("LocKeyConverter", app);
        Assert.Contains("BoolToFlowDirectionConverter", app);
    }

    /// <summary>
    /// Every resource referenced anywhere in MainWindow.xaml must be declared in a
    /// scope that exists at load time: App.xaml, Window.Resources, or the brand
    /// theme dictionaries (light/dark share the same key set).
    /// </summary>
    [Fact]
    public void AllMainWindow_ResourceReferences_AreDeclaredSomewhere()
    {
        var window = Read("src/AusbildungsWegInterview.App/MainWindow.xaml");
        var app = Read("src/AusbildungsWegInterview.App/App.xaml");
        var theme = Read("src/AusbildungsWegInterview.App/Themes/AwTheme.Light.xaml");
        var themeDark = Read("src/AusbildungsWegInterview.App/Themes/AwTheme.Dark.xaml");

        var refs = StaticResourceRefsIn(window).Concat(DynamicResourceRefsIn(window)).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(refs);

        var declared = DeclaredKeys(app).Concat(DeclaredKeys(window)).Concat(DeclaredKeys(theme)).ToHashSet(StringComparer.Ordinal);
        var missing = refs.Except(declared).OrderBy(x => x).ToList();
        Assert.Empty(missing); // would crash at XAML load with "Cannot find resource named ..."

        // Light and dark themes must offer the same keys (theme switch swaps one for the other).
        Assert.Equal(DeclaredKeys(theme), DeclaredKeys(themeDark));
    }
}
