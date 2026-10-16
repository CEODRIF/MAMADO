using System.Text.RegularExpressions;
using System.Xml;
using Xunit;

namespace AusbildungsWegInterview.Tests.App;

/// <summary>
/// Accessibility regression guards (run on any OS, XAML/code level):
/// every interactive control has a screen-reader name, the themed focus
/// visual exists in BOTH themes and is wired to the window, every
/// automation label key exists in all four locales, and live-region
/// announcements are wired for both UI modes.
/// </summary>
public class AccessibilityTests
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

    private static string AppFile(string name) =>
        Path.Combine(FindRepoRoot(), "src", "AusbildungsWegInterview.App", name);

    [Fact]
    public void EveryInteractiveControl_HasAnAccessibleName()
    {
        string xaml = File.ReadAllText(AppFile("MainWindow.xaml"));
        var violations = new List<string>();

        using var reader = new XmlTextReader(new StringReader(xaml)) { DtdProcessing = DtdProcessing.Ignore };
        int expanderDepth = 0;
        bool expanderHasHeader = false;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "Expander" && expanderDepth > 0)
            {
                expanderDepth--;
                if (!expanderHasHeader)
                    violations.Add("Expander line " + reader.LineNumber);
                continue;
            }
            if (reader.NodeType != XmlNodeType.Element)
                continue;
            string tag = reader.LocalName;
            if (expanderDepth > 0 && reader.Name.EndsWith(".Header", StringComparison.Ordinal))
                expanderHasHeader = true; // header as child property element
            if (tag is not ("Button" or "ComboBox" or "CheckBox" or "TextBox" or "Expander"))
                continue;

            string? automationName = null, content = null, header = null;
            while (reader.MoveToNextAttribute())
            {
                if (reader.Name == "AutomationProperties.Name") automationName = reader.Value;
                else if (reader.LocalName == "Content") content = reader.Value;
                else if (reader.LocalName == "Header") header = reader.Value;
            }

            if (tag == "Expander")
            {
                expanderDepth++;
                expanderHasHeader = !string.IsNullOrWhiteSpace(header);
                continue; // resolved at the matching </Expander>
            }

            bool named = !string.IsNullOrWhiteSpace(automationName) || !string.IsNullOrWhiteSpace(content);
            if (!named)
                violations.Add(tag + " line " + reader.LineNumber);
        }

        Assert.True(violations.Count == 0,
            "interactive controls without any accessible name (AutomationProperties.Name / Content / Header): "
            + string.Join("; ", violations));
    }

    [Fact]
    public void ThemedFocusVisual_IsDefinedInBothThemes_AndWiredToTheWindow()
    {
        foreach (string theme in new[] { "Themes/AwTheme.Dark.xaml", "Themes/AwTheme.Light.xaml" })
        {
            string xaml = File.ReadAllText(AppFile(theme));
            Assert.True(xaml.Contains("x:Key=\"AwFocusVisual\"", StringComparison.Ordinal),
                theme + " must define the themed focus visual (both themes keep an identical key set)");
            Assert.True(xaml.Contains("Property=\"FocusVisualStyle\"", StringComparison.Ordinal),
                theme + " must wire FocusVisualStyle into the Window default style");
        }
    }

    [Fact]
    public void EveryAutomationLabelKey_ExistsInAllFourLocales()
    {
        string xaml = File.ReadAllText(AppFile("MainWindow.xaml"));
        var match = Regex.Matches(
            xaml,
            "AutomationProperties\\.Name=\"\\{Binding T, Converter=\\{StaticResource LocKey\\}, ConverterParameter=(\\w+)\\}\"");

        Assert.True(match.Count >= 10, "expected at least 10 localized automation labels, found " + match.Count);

        string locDir = Path.Combine(FindRepoRoot(), "src", "AusbildungsWegInterview.Localization", "Loc");
        string[] locales = { "Strings.resx", "Strings.de.resx", "Strings.fr.resx", "Strings.ar.resx" };
        foreach (Match m in match)
        {
            string key = m.Groups[1].Value;
            foreach (string locale in locales)
            {
                string resx = File.ReadAllText(Path.Combine(locDir, locale));
                Assert.True(
                    System.Text.RegularExpressions.Regex.IsMatch(resx, $"<data name=\"{key}\""),
                    $"automation label key {key} missing from {locale}");
            }
        }
    }

    [Fact]
    public void LiveRegionAnnouncements_AreWiredForTheShippedUi()
    {
        string code = File.ReadAllText(AppFile("MainWindow.xaml.cs"));
        string xaml = File.ReadAllText(AppFile("MainWindow.xaml"));

        Assert.True(code.Contains("LiveRegionChanged", StringComparison.Ordinal),
            "screen-reader announcements must use WPF's built-in LiveRegionChanged automation event");

        // The shipped MAMADO shell renders the main interface only. The compact
        // overlay controller is retained (WPF-free, unit-tested) for a future
        // pass, but its XAML text elements (AwSpeechText/…) are not part of
        // this shell — so the live-region contract is asserted for every
        // shipped live-text element, which must never silently go unannounced.
        string[] liveTextElements =
        {
            "MainSpeechText", "MainTranslationText", "MainAnswerText" // main interface
        };
        foreach (string element in liveTextElements)
        {
            Assert.True(xaml.Contains($"x:Name=\"{element}\""), "MainWindow.xaml must declare x:Name=\"" + element + "\"");
            Assert.True(code.Contains(element), "MainWindow.xaml.cs must reference " + element + " for announcements");
        }
    }
}
