namespace AusbildungsWegInterview.App;

/// <summary>Device-independent (WPF unit) rectangle.</summary>
public readonly record struct RectD(int Left, int Top, int Width, int Height);

/// <summary>Which window mode is active.</summary>
public enum OverlayMode
{
    Main,
    Interview
}

/// <summary>
/// WPF-free state machine + geometry for the compact interview overlay.
/// Decides (never renders): mode transitions, the corner-placed target
/// rectangle, work-area clamping (the overlay can never be positioned
/// ENTIRELY outside the visible work area) and min/max size bounds.
/// The WPF layer applies the targets and animates them; everything here is
/// pure arithmetic, so it is unit-testable on any OS.
/// </summary>
public sealed class InterviewOverlayController
{
    public const int DefaultInterviewWidth = 420;
    public const int DefaultInterviewHeight = 300;

    /// <summary>Initial compact size (DIU); the user can resize within Min/Max.</summary>
    public int InterviewWidth { get; init; } = DefaultInterviewWidth;
    public int InterviewHeight { get; init; } = DefaultInterviewHeight;

    /// <summary>Reasonable resize bounds for the compact overlay.</summary>
    public int MinWidth { get; init; } = 340;
    public int MinHeight { get; init; } = 240;
    public int MaxWidth { get; init; } = 720;
    public int MaxHeight { get; init; } = 520;

    /// <summary>Inset from the work-area edges for the corner placement.</summary>
    public int EdgeMargin { get; init; } = 28;

    public OverlayMode Mode { get; private set; } = OverlayMode.Main;

    /// <summary>
    /// True between a SUCCESSFUL pipeline start and the session end.
    /// The compact transition is only allowed while armed, so a failed
    /// start can never leave the app in a misleading listening state.
    /// </summary>
    public bool SessionArmed { get; private set; }

    private RectD _workArea;
    private RectD _target;
    private RectD? _savedMain;

    /// <summary>The rectangle the window should move to (current target).</summary>
    public RectD Target => _target;

    /// <summary>The saved main-window rectangle (restored on exit); null before the first enter.</summary>
    public RectD? SavedMainTarget => _savedMain;

    public event EventHandler? ModeChanged;
    public event EventHandler? TargetChanged;

    public void SetWorkArea(RectD area) => _workArea = area;

    /// <summary>Called when the pipeline start ACTUALLY succeeded.</summary>
    public void ArmSession() => SessionArmed = true;

    /// <summary>Called on stop, fault, or start failure.</summary>
    public void DisarmSession() => SessionArmed = false;

    /// <summary>
    /// Enters interview mode only for an armed session. Saves the current
    /// main-mode rectangle and returns the compact target (bottom-right
    /// corner of the work area, inset by EdgeMargin). Returns null when the
    /// transition is not allowed (already in interview mode, or the session
    /// was not armed - e.g. a failed start).
    /// </summary>
    public RectD? TryEnterInterviewMode(int mainLeft, int mainTop, int mainWidth, int mainHeight)
    {
        if (Mode == OverlayMode.Interview || !SessionArmed)
            return null;

        _savedMain = new RectD(mainLeft, mainTop, mainWidth, mainHeight);
        _target = InterviewTargetAt(InterviewWidth, InterviewHeight);
        Mode = OverlayMode.Interview;
        ModeChanged?.Invoke(this, EventArgs.Empty);
        TargetChanged?.Invoke(this, EventArgs.Empty);
        return _target;
    }

    /// <summary>
    /// Leaves interview mode. Returns the saved main-mode rectangle (the
    /// window animates back to it) or null when not in interview mode.
    /// </summary>
    public RectD? ExitToMainMode()
    {
        if (Mode != OverlayMode.Interview)
            return null;

        RectD? saved = _savedMain;
        Mode = OverlayMode.Main;
        _savedMain = null;
        DisarmSession();
        if (saved is { } t)
        {
            _target = t;
            TargetChanged?.Invoke(this, EventArgs.Empty);
        }
        ModeChanged?.Invoke(this, EventArgs.Empty);
        return saved;
    }

    /// <summary>Corner placement for a given (clamped) size: bottom-right of the work area.</summary>
    public RectD InterviewTargetAt(int width, int height)
    {
        var (w, h) = ClampSize(width, height);
        int left = _workArea.Left + _workArea.Width - w - EdgeMargin;
        int top = _workArea.Top + _workArea.Height - h - EdgeMargin;
        return new RectD(left, top, w, h);
    }

    /// <summary>
    /// Clamps a position so the given size stays FULLY inside the work area
    /// (the anti-off-screen invariant). If the work area is smaller than the
    /// window, the window is pinned to the work-area's top-left.
    /// </summary>
    public RectD ClampPosition(int left, int top, int width, int height)
    {
        var (w, h) = ClampSize(width, height);
        int minLeft = _workArea.Left;
        int maxLeft = _workArea.Left + _workArea.Width - w;
        int minTop = _workArea.Top;
        int maxTop = _workArea.Top + _workArea.Height - h;
        if (maxLeft < minLeft) maxLeft = minLeft;
        if (maxTop < minTop) maxTop = minTop;
        return new RectD(
            Math.Clamp(left, minLeft, maxLeft),
            Math.Clamp(top, minTop, maxTop),
            w, h);
    }

    /// <summary>Clamps a size into the interview min/max bounds.</summary>
    public (int Width, int Height) ClampSize(int width, int height)
        => (Math.Clamp(width, MinWidth, MaxWidth), Math.Clamp(height, MinHeight, MaxHeight));
}
