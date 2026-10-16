using System.Xml;
using Xunit;

namespace AusbildungsWegInterview.Tests.App;

/// <summary>
/// Branding contract (file-parsing tests, same approach as AccessibilityTests —
/// the WPF runtime itself is Windows-only).
///
/// The product is named "AusbildungsWeg Interview" with the tagline
/// "AI Interview Assistant". The legacy brand "MAMADO" (an internal design
/// codename) must not surface in user-facing UI sources. These tests pin the
/// visible brand so the old name cannot silently come back.
///
/// Intentionally NOT covered here: repository/branch names, .NET namespaces,
/// project directories, and the whisper.cpp test helper that locates the local
/// checkout by its directory name — internal identifiers that must stay stable.
/// </summary>
public class BrandingTests
{
    private const string ProductName = "AusbildungsWeg Interview";

    private static readonly string[] ResxFiles =
    {
        "Strings.resx", "Strings.de.resx", "Strings.fr.resx", "Strings.ar.resx"
    };

    [Fact]
    public void WindowTitle_Key_IsTheProductName_InAllLocales()
    {
        foreach (var file in ResxFiles)
        {
            var value = ResxValue(file, "TitleApp");
            Assert.True(value == ProductName,
                $"{file}: TitleApp is '{value}', expected '{ProductName}'");
        }
    }

    [Fact]
    public void HeaderTagline_UsesAssistantNotCopilot_InAllLocales()
    {
        foreach (var file in ResxFiles)
        {
            var value = ResxValue(file, "AppTagline");
            Assert.False(value.Contains("Copilot", StringComparison.OrdinalIgnoreCase),
                $"{file}: AppTagline still contains the old brand term: '{value}'");
        }
        // The English catalog holds the canonical tagline.
        Assert.Equal("AI Interview Assistant", ResxValue("Strings.resx", "AppTagline"));
    }

    [Fact]
    public void MainWindow_Xaml_HasNoLegacyBrand_AndHeaderUsesTheProductName()
    {
        string xaml = File.ReadAllText(AppFile("MainWindow.xaml"));

        Assert.False(xaml.Contains("MAMADO", StringComparison.OrdinalIgnoreCase),
            "MainWindow.xaml still references the legacy brand 'MAMADO' in user-facing UI");
        Assert.True(xaml.Contains("Title=\"{Binding T, Converter={StaticResource LocKey}, ConverterParameter=TitleApp}\"", StringComparison.Ordinal),
            "the window title bar must use the TitleApp key");
        Assert.True(xaml.Contains("ConverterParameter=TitleApp", StringComparison.Ordinal)
                     && xaml.Contains("FontWeight=\"Bold\"", StringComparison.Ordinal),
            "the main header must display the product name bound to the TitleApp key");
    }

    [Fact]
    public void CopiedDiagnostics_Header_UsesTheProductName()
    {
        string vm = File.ReadAllText(AppFile("ViewModels/MainViewModel.cs"));
        Assert.True(vm.Contains("AppendLine(\"AusbildungsWeg Interview\")", StringComparison.Ordinal),
            "the diagnostics header line (copied by the user on the Feedback page) must be the product name");
        Assert.False(vm.Contains("AppendLine(\"MAMADO", StringComparison.OrdinalIgnoreCase),
            "the diagnostics header still carries the legacy brand");
    }

    private static string ResxValue(string file, string key)
    {
        var doc = new XmlDocument();
        doc.Load(ResxFile(file));
        var root = doc.DocumentElement ?? throw new InvalidOperationException($"resx has no root element: {file}");
        var mgr = new XmlNamespaceManager(doc.NameTable);
        mgr.AddNamespace("", root.NamespaceURI);
        var node = doc.SelectSingleNode($"//*[local-name()='data' and @name='{key}']/*[local-name()='value']", mgr);
        return node?.InnerText
               ?? throw new InvalidOperationException($"{file}: key '{key}' not found");
    }

    private static string ResxFile(string name) =>
        Path.Combine(FindRepoRoot(), "src", "AusbildungsWegInterview.Localization", "Loc", name);

    private static string AppFile(string name) =>
        Path.Combine(FindRepoRoot(), "src", "AusbildungsWegInterview.App", name);

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
}
