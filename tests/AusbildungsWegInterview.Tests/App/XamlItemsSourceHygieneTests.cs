using System.Xml;
using Xunit;

namespace AusbildungsWegInterview.Tests.App;

/// <summary>
/// XAML structural regression guards (run on any OS, no WPF runtime needed).
///
/// Background: the app once crashed at startup on Windows with
/// "Items collection must be empty before using ItemsSource." The root cause
/// was a line that LOOKED like an attribute (ItemContainerStyle="…") but sat
/// AFTER the control's opening tag was closed — i.e. a text node inside the
/// ListBox. WPF treats such text as an item added to the control's Items
/// collection, and adding items to a control that also gets an ItemsSource
/// binding is a startup-time InvalidOperationException. That defect is
/// well-formed XML, so `dotnet build` and plain XML parsing do not catch it —
/// these tests do.
/// </summary>
public class XamlItemsSourceHygieneTests
{
    private static readonly string[] XamlFiles =
    {
        "MainWindow.xaml",
        "App.xaml",
        "Themes/AwTheme.Dark.xaml",
        "Themes/AwTheme.Light.xaml"
    };

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

    /// <summary>
    /// The only elements with legitimate TEXT content in this app's XAML are
    /// color definitions (<Color>#FF0000</Color>). Any other non-whitespace
    /// text node is a defect — most commonly an attribute line written after
    /// the opening tag was closed ("orphan attribute"), which WPF would add
    /// to an ItemsControl's Items collection and crash at startup.
    /// </summary>
    [Theory]
    [MemberData(nameof(XamlFilesData))]
    public void NoOrphanAttributeTextNodes_Anywhere(string file)
    {
        var doc = new XmlDocument();
        doc.Load(AppFile(file));
        var root = doc.DocumentElement ?? throw new InvalidOperationException("XAML has no root element: " + file);

        var offenders = new List<string>();
        CollectTextOffenders(root, offenders);
        Assert.True(offenders.Count == 0,
            "Non-whitespace text content found (an attribute written after the closing '>' of a tag renders as text " +
            $"and crashes WPF at runtime — move it INSIDE the opening tag):\n  " +
            string.Join("\n  ", offenders));
    }

    public static IEnumerable<object[]> XamlFilesData =>
        XamlFiles.Select(f => new object[] { f });

    /// <summary>
    /// Every control that receives an ItemsSource must have NO content
    /// children (items) — only property elements (ItemsPanel, ItemTemplate,
    /// ItemContainerStyle, …). Mixing declared items with an ItemsSource
    /// binding is exactly the "Items collection must be empty before using
    /// ItemsSource" startup crash.
    /// </summary>
    [Theory]
    [MemberData(nameof(XamlFilesData))]
    public void ItemsSourceControls_HaveNoDeclaredItems(string file)
    {
        var doc = new XmlDocument();
        doc.Load(AppFile(file));
        var root = doc.DocumentElement ?? throw new InvalidOperationException("XAML has no root element: " + file);

        var offenders = new List<string>();
        CheckItemsSourceControls(root, offenders);
        Assert.True(offenders.Count == 0,
            "ItemsSource control with declared content (items) found — WPF throws " +
            "'Items collection must be empty before using ItemsSource' at startup:\n  " +
            string.Join("\n  ", offenders));
    }

    private static void CollectTextOffenders(XmlElement element, List<string> offenders)
    {
        foreach (XmlNode child in element.ChildNodes)
        {
            switch (child.NodeType)
            {
                case XmlNodeType.Text when !string.IsNullOrWhiteSpace(child.Value):
                    if (element.LocalName != "Color")
                        offenders.Add($"<{element.LocalName}>: '{TrimTo(child.Value, 60)}'");
                    break;
                case XmlNodeType.Element:
                    CollectTextOffenders((XmlElement)child, offenders);
                    break;
            }
        }
    }

    private static void CheckItemsSourceControls(XmlElement element, List<string> offenders)
    {
        if (element.HasAttribute("ItemsSource"))
        {
            foreach (XmlNode child in element.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element)
                    continue;
                var ce = (XmlElement)child;
                // Property-element syntax ("Owner.Property") is configuration, not an item.
                if (ce.LocalName.StartsWith(element.LocalName + ".", StringComparison.Ordinal))
                    continue;
                offenders.Add($"<{element.LocalName} ItemsSource=…> contains declared item <{ce.LocalName}>");
            }
        }

        foreach (XmlNode child in element.ChildNodes)
        {
            if (child.NodeType == XmlNodeType.Element)
                CheckItemsSourceControls((XmlElement)child, offenders);
        }
    }

    private static string TrimTo(string s, int max)
    {
        s = s.Trim();
        return s.Length <= max ? s : s[..max] + "…";
    }
}
