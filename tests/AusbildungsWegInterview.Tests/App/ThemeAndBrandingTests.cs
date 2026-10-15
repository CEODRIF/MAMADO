using System.Text.RegularExpressions;
using Xunit;

namespace AusbildungsWegInterview.Tests.App;

/// <summary>
/// Theme consistency + branding asset reference tests (file-based; no WPF
/// runtime needed). Guards the two regression classes fixed in the dark-theme
/// redesign: near-black/white-misapplication palettes, and missing/unpackaged
/// branding assets.
/// </summary>
public class ThemeAndBrandingTests
{
    private static string RepoRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string Read(string relPath)
        => File.ReadAllText(Path.Combine(RepoRoot(), relPath.Replace('/', Path.DirectorySeparatorChar)));

    private static double Luminance(string hex)
    {
        var h = hex.TrimStart('#');
        byte r = Convert.ToByte(h[0..2], 16), g = Convert.ToByte(h[2..4], 16), b = Convert.ToByte(h[4..6], 16);
        return (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
    }

    private static string ThemeColor(string theme, string key)
    {
        // <SolidColorBrush x:Key="k" Color="#RRGGBB"/> attribute form
        var m = Regex.Match(theme, $@"x:Key=""{key}""[^>]*Color=""(#[0-9A-Fa-f]{{6}})""");
        if (m.Success)
            return m.Groups[1].Value;
        // <Color x:Key="k">#RRGGBB</Color> element-content form
        var m2 = Regex.Match(theme, $@"<Color x:Key=""{key}"">([#][0-9A-Fa-f]{{6}})</Color>");
        Assert.True(m2.Success, $"{key} not found in theme");
        return m2.Groups[1].Value;
    }

    [Fact]
    public void DarkTheme_IsRefinedDark_NeitherNearBlackNorPureWhite()
    {
        var dark = Read("src/AusbildungsWegInterview.App/Themes/AwTheme.Dark.xaml");

        // No pure white surfaces anywhere in the dark theme.
        Assert.DoesNotContain("\"#FFFFFF\"", dark, StringComparison.OrdinalIgnoreCase);

        // Background is genuinely dark but NOT near-black (refined navy/slate).
        var bg = Luminance(ThemeColor(dark, "AwBackgroundColor"));
        Assert.InRange(bg, 0.08, 0.30);

        // Primary text is light (readable on the dark background).
        Assert.True(Luminance(ThemeColor(dark, "AwInkBrush")) > 0.65, "dark-theme ink must be light");
    }

    [Fact]
    public void DarkTheme_SurfacesAreLayeredAndDistinct()
    {
        var dark = Read("src/AusbildungsWegInterview.App/Themes/AwTheme.Dark.xaml");

        var bg = ThemeColor(dark, "AwBackgroundColor");
        var surface = ThemeColor(dark, "AwSurfaceBrush");
        var surface2 = ThemeColor(dark, "AwSurface2Brush");

        Assert.NotEqual(bg, surface);
        Assert.NotEqual(bg, surface2);
        Assert.NotEqual(surface, surface2);

        // Hierarchy: cards lift above the background, panels lift above cards.
        Assert.True(Luminance(surface) > Luminance(bg), "cards must be lighter than the window background");
        Assert.True(Luminance(surface2) > Luminance(surface), "inner panels must be lighter than cards");

        // Secondary text must stay distinct from primary text and background.
        var inkSoft = ThemeColor(dark, "AwInkSoftBrush");
        var muted = ThemeColor(dark, "AwMutedBrush");
        Assert.NotEqual(inkSoft, bg);
        Assert.NotEqual(muted, bg);
        Assert.NotEqual(inkSoft, muted);
    }

    [Fact]
    public void BothThemes_DefineEveryBrushTheWindowReferences()
    {
        var light = Read("src/AusbildungsWegInterview.App/Themes/AwTheme.Light.xaml");
        var dark = Read("src/AusbildungsWegInterview.App/Themes/AwTheme.Dark.xaml");
        var window = Read("src/AusbildungsWegInterview.App/MainWindow.xaml");

        var themeKeys = Regex.Matches(light, @"x:Key=""(Aw[A-Za-z0-9_]+)""").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(themeKeys);

        var darkKeys = Regex.Matches(dark, @"x:Key=""(Aw[A-Za-z0-9_]+)""").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(themeKeys, darkKeys); // theme switch must never lose a resource

        var windowRefs = Regex.Matches(window, @"\{DynamicResource\s+(Aw[A-Za-z0-9_]+)\}")
            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        // A reference may resolve from the theme dictionaries OR from the
        // window's own resources (e.g. the AwMiniLabel/AwChipText styles).
        var windowKeys = Regex.Matches(window, @"x:Key=""(Aw[A-Za-z0-9_]+)""").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var resolvable = themeKeys.Concat(windowKeys).ToHashSet(StringComparer.Ordinal);
        var missing = windowRefs.Except(resolvable).ToList();
        Assert.True(missing.Count == 0, $"window references unknown resources: {string.Join(", ", missing)}");
    }

    /// <summary>Regression: WPF ScrollViewer is white by default - the main body must opt into the theme.</summary>
    [Fact]
    public void MainWindow_ScrollViewer_UsesThemedBackground()
    {
        var window = Read("src/AusbildungsWegInterview.App/MainWindow.xaml");
        Assert.Matches(
            @"<ScrollViewer\s+Background=""\{DynamicResource\s+Aw[A-Za-z0-9_]+\}""",
            window);
    }

    [Fact]
    public void Logo_Asset_IsSquarePngOfSensibleSize()
    {
        var path = Path.Combine(RepoRoot(), "src", "AusbildungsWegInterview.App", "Assets", "AwLogo.png");
        Assert.True(File.Exists(path), "AwLogo.png must be committed in the repository");
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 10_000, "logo looks too small/corrupt");

        // PNG signature + IHDR dimensions (must be 1:1 - the header must never distort it).
        Assert.True(bytes.Length > 24
            && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47);
        int w = bytes[16] << 24 | bytes[17] << 16 | bytes[18] << 8 | bytes[19];
        int h = bytes[20] << 24 | bytes[21] << 16 | bytes[22] << 8 | bytes[23];
        Assert.Equal(w, h);
        Assert.InRange(w, 256, 2048);
    }

    [Fact]
    public void AppIcon_Asset_IsValidMultiSizeIco()
    {
        var path = Path.Combine(RepoRoot(), "src", "AusbildungsWegInterview.App", "Assets", "app.ico");
        Assert.True(File.Exists(path), "app.ico must be committed in the repository");
        var bytes = File.ReadAllBytes(path);

        // ICO header: reserved=0, type=1, count>=3
        Assert.Equal(0, bytes[0]);
        Assert.Equal(0, bytes[1]);
        Assert.Equal(1, bytes[2]);
        int count = bytes[4] | bytes[5] << 8;
        Assert.True(count >= 3, "icon should carry multiple sizes");

        var sizes = new List<int>();
        for (int i = 0; i < count; i++)
        {
            int off = 6 + i * 16;
            int s = bytes[off];
            sizes.Add(s == 0 ? 256 : s);
        }
        Assert.Contains(16, sizes);
        Assert.Contains(32, sizes);
        Assert.Contains(256, sizes);
    }

    [Fact]
    public void Project_WiresIconAndLogo_AndWindowReferencesLogo()
    {
        var csproj = Read("src/AusbildungsWegInterview.App/AusbildungsWegInterview.App.csproj");
        Assert.Matches(@"<ApplicationIcon>[^<]*app\.ico</ApplicationIcon>", csproj);
        Assert.Matches(@"<Resource Include=""[^<]*AwLogo\.png""", csproj);

        var window = Read("src/AusbildungsWegInterview.App/MainWindow.xaml");
        Assert.Contains("Source=\"/Assets/AwLogo.png\"", window);
    }
}
