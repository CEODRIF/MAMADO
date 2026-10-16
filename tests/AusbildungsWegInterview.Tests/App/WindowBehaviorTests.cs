using System.Text.RegularExpressions;
using Xunit;

namespace AusbildungsWegInterview.Tests.App;

/// <summary>
/// WPF-free static guard for the window's compact/fixed design and the
/// interview-mode wiring in MainWindow.xaml (the runtime behavior itself is
/// Windows-only; this prevents accidental regressions like a resize mode or
/// a lost IsInterviewMode binding coming back in a future UI pass).
/// </summary>
public class WindowBehaviorTests
{
    private static readonly string Xaml =
        File.ReadAllText(Path.Combine(FindRepoRoot(),
            "src", "AusbildungsWegInterview.App", "MainWindow.xaml"));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AusbildungsWegInterview.sln")))
            dir = dir.Parent;
        Assert.True(dir is not null, "repository root (AusbildungsWegInterview.sln) not found above the test assembly");
        return dir!.FullName;
    }

    [Fact]
    public void Window_IsCompact_FixedSize_NoResize()
    {
        var windowTag = Regex.Match(Xaml, @"<Window\b[^>]*>", RegexOptions.Singleline);
        Assert.True(windowTag.Success, "Window start tag not found.");
        string tag = windowTag.Value;

        Assert.Contains("ResizeMode=\"NoResize\"", tag);
        Assert.DoesNotContain("MinWidth", tag); // fixed size: no resize affordances
        Assert.DoesNotContain("MinHeight", tag);
        Assert.Contains("WindowStartupLocation=\"CenterScreen\"", tag);

        var width = int.Parse(Regex.Match(tag, @"Width=""(\d+)""").Groups[1].Value);
        var height = int.Parse(Regex.Match(tag, @"Height=""(\d+)""").Groups[1].Value);
        // Compact, laptop-friendly at 100% DPI (fits 1366x768 incl. title bar).
        Assert.InRange(width, 900, 1100);
        Assert.InRange(height, 620, 780);
    }

    [Fact]
    public void InterviewMode_HidesSetupAndKeepsLiveContent()
    {
        // The mode flag must actually drive the UI: hidden setup surfaces and
        // the nav rail filtering, while the live trios + transcript remain.
        int inverseModeRefs = Regex.Matches(Xaml, @"IsInterviewMode, Converter=\{StaticResource InverseBoolToVis\}").Count;
        Assert.True(inverseModeRefs >= 5,
            $"expected at least 5 inverse-mode hidden surfaces (nav secondary, header shortcuts, device combo, refresh, answer controls, documents, access code), found {inverseModeRefs}.");

        Assert.Contains("ItemsSource=\"{Binding VisibleNavPrimary}\"", Xaml); // per-item rail filtering (interview only)
        Assert.Contains("{Binding AccessCode, UpdateSourceTrigger=PropertyChanged}", Xaml); // access-code field bound
        // The LIVE surfaces are the interview mode itself and must stay rendered
        // (recognized speech trio, translation within it, suggested-answer trio,
        // live transcript):
        Assert.Contains("ItemsSource=\"{Binding QuestionPanels}\"", Xaml);
        Assert.Contains("ItemsSource=\"{Binding AnswerPanels}\"", Xaml);
        Assert.Contains("{Binding TranscriptText}", Xaml);
    }

    [Fact]
    public void Stop_RemainsBound_And_Start_OnlyWhenNotListening()
    {
        Assert.Contains("Command=\"{Binding StopCommand}\"", Xaml);
        var startButton = Regex.Match(Xaml, @"<Button[^>]*Command=""\{Binding StartCommand\}""[^>]*>", RegexOptions.Singleline);
        Assert.True(startButton.Success, "Start button not found.");
        Assert.Contains("IsListening, Converter={StaticResource InverseBoolToVis}", startButton.Value);
    }
}