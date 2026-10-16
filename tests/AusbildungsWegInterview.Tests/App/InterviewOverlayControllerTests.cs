using AusbildungsWegInterview.App;
using Xunit;

namespace AusbildungsWegInterview.Tests.App;

/// <summary>
/// The compact interview overlay's state machine + geometry (WPF-free, so it
/// runs on any OS): transitions are only allowed for a session whose start
/// ACTUALLY succeeded, corner placement uses the work area, the overlay can
/// never be placed entirely outside the visible area, and sizes stay within
/// the min/max bounds.
/// </summary>
public class InterviewOverlayControllerTests
{
    private static InterviewOverlayController NewController()
    {
        var c = new InterviewOverlayController();
        // A 1920x1080 screen with a 40px work-area inset (taskbar-like).
        c.SetWorkArea(new RectD(40, 40, 1840, 960));
        return c;
    }

    [Fact]
    public void Enter_WithoutArmedSession_DoesNothing()
    {
        var c = NewController();
        bool modeChanged = false;
        c.ModeChanged += (_, _) => modeChanged = true;

        var target = c.TryEnterInterviewMode(0, 0, 640, 840);

        Assert.Null(target);
        Assert.Equal(OverlayMode.Main, c.Mode);
        Assert.False(modeChanged, "a failed start must never trigger the compact transition");
    }

    [Fact]
    public void Enter_AfterArmedSession_PlacesAtBottomRightCorner()
    {
        var c = NewController();
        c.ArmSession();
        var target = c.TryEnterInterviewMode(10, 20, 640, 840);

        Assert.NotNull(target);
        Assert.Equal(OverlayMode.Interview, c.Mode);
        // 40+1840-420-28 = 1432 ; 40+960-300-28 = 672
        Assert.Equal(new RectD(1432, 672, 420, 300), target!.Value);
        Assert.Equal(new RectD(10, 20, 640, 840), c.SavedMainTarget!.Value);
        Assert.Equal(target.Value, c.Target);
    }

    [Fact]
    public void Enter_WhileAlreadyInInterviewMode_IsNoOp()
    {
        var c = NewController();
        c.ArmSession();
        Assert.NotNull(c.TryEnterInterviewMode(0, 0, 640, 840));

        var second = c.TryEnterInterviewMode(999, 999, 999, 999);

        Assert.Null(second);
        Assert.True(c.SavedMainTarget == new RectD(0, 0, 640, 840), "the original main rect must not be clobbered");
    }

    [Fact]
    public void Exit_RestoresSavedMainRect_AndDisarms()
    {
        var c = NewController();
        c.ArmSession();
        c.TryEnterInterviewMode(10, 20, 640, 840);

        var saved = c.ExitToMainMode();

        Assert.Equal(new RectD(10, 20, 640, 840), saved!.Value);
        Assert.Equal(OverlayMode.Main, c.Mode);
        Assert.False(c.SessionArmed);
        Assert.Null(c.SavedMainTarget);
        Assert.True(c.ExitToMainMode() is null, "exiting twice is a no-op");
    }

    [Fact]
    public void ModeChanged_FiresExactlyOncePerTransition()
    {
        var c = NewController();
        var sequence = new List<OverlayMode>();
        c.ModeChanged += (_, _) => sequence.Add(c.Mode);
        c.ArmSession();
        c.TryEnterInterviewMode(0, 0, 640, 840);
        c.TryEnterInterviewMode(0, 0, 640, 840); // guarded
        c.ExitToMainMode();
        c.ExitToMainMode(); // guarded

        Assert.Equal(new[] { OverlayMode.Interview, OverlayMode.Main }, sequence);
    }

    [Theory]
    [InlineData(-5000, -5000)] // far off-screen top-left
    [InlineData(100000, 100000)] // far off-screen bottom-right
    [InlineData(40, 2000)] // below the work area
    [InlineData(100000, 40)] // right of the work area
    [InlineData(40, 40)] // exactly at the corner (already valid)
    public void ClampPosition_NeverPlacesTheOverlayEntirelyOutsideTheWorkArea(int left, int top)
    {
        var c = NewController();
        var r = c.ClampPosition(left, top, 420, 300);

        Assert.True(r.Left >= 40, $"Left {r.Left} < work area left");
        Assert.True(r.Top >= 40, $"Top {r.Top} < work area top");
        Assert.True(r.Left + r.Width <= 40 + 1840, $"right edge {r.Left + r.Width} exceeds work area");
        Assert.True(r.Top + r.Height <= 40 + 960, $"bottom edge {r.Top + r.Height} exceeds work area");
    }

    [Fact]
    public void ClampPosition_WorkAreaSmallerThanWindow_PinsToTopLeft()
    {
        var c = new InterviewOverlayController();
        c.SetWorkArea(new RectD(100, 100, 200, 150)); // smaller than any overlay

        var r = c.ClampPosition(5000, 5000, 420, 300);

        Assert.Equal(100, r.Left);
        Assert.Equal(100, r.Top);
    }

    [Theory]
    [InlineData(200, 100, 340, 240)]   // below min -> min
    [InlineData(9999, 9999, 720, 520)] // above max -> max
    [InlineData(500, 400, 500, 400)]   // in range -> unchanged
    [InlineData(340, 520, 340, 520)]   // exactly at bounds
    public void ClampSize_RespectsInterviewBounds(int w, int h, int expectedW, int expectedH)
    {
        var c = NewController();

        var (cw, ch) = c.ClampSize(w, h);

        Assert.Equal(expectedW, cw);
        Assert.Equal(expectedH, ch);
    }

    [Fact]
    public void InterviewTargetAt_UsesClampedSizeForPlacement()
    {
        var c = NewController();

        var r = c.InterviewTargetAt(9999, 9999);

        Assert.Equal(720, r.Width);
        Assert.Equal(520, r.Height);
        Assert.Equal(40 + 1840 - 720 - 28, r.Left);
        Assert.Equal(40 + 960 - 520 - 28, r.Top);
    }
}
