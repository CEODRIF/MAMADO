using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AusbildungsWegInterview.App.ViewModels;

namespace AusbildungsWegInterview.App;

/// <summary>
/// The single floating window. Code-behind stays view-only (MVVM): it builds
/// the view model, then applies the overlay geometry that the WPF-FREE
/// <see cref="InterviewOverlayController"/> decides - animated mode
/// transitions (window size/position), work-area clamping so the compact
/// overlay can never be placed entirely outside the visible area, and
/// subtle one-shot entrance effects for new content.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private Storyboard? _windowAnimation;

    public MainWindow(AppServices services)
    {
        InitializeComponent();
        _viewModel = new MainViewModel(services, Dispatcher);
        DataContext = _viewModel;
        _viewModel.InterviewStarted += OnInterviewStarted;
        _viewModel.InterviewSessionEnded += OnInterviewSessionEnded;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += (_, _) => SyncWorkArea();
    }

    private void SyncWorkArea()
    {
        var wa = SystemParameters.WorkArea; // device-independent units
        _viewModel.Overlay.SetWorkArea(new RectD((int)wa.Left, (int)wa.Top, (int)wa.Width, (int)wa.Height));
    }

    // ---------- mode transitions: animate the window to the decided target ----------

    private void OnInterviewStarted(object? sender, EventArgs e)
    {
        SyncWorkArea();
        // Only a start that ACTUALLY succeeded raises this event; the
        // controller additionally guards with its armed-session flag.
        if (_viewModel.Overlay.TryEnterInterviewMode((int)Left, (int)Top, (int)Width, (int)Height) is { } target)
            ApplyTarget(target);
    }

    private void OnInterviewSessionEnded(object? sender, EventArgs e)
    {
        if (_viewModel.Overlay.ExitToMainMode() is { } saved)
            ApplyTarget(saved);
    }

    /// <summary>
    /// Animates Width/Height/Left/Top to the target (one eased storyboard;
    /// a new transition stops the previous one - interruption handled).
    /// With reduced-motion preference, values are set directly.
    /// </summary>
    private void ApplyTarget(RectD target)
    {
        if (!_viewModel.AnimationsEnabled)
        {
            Width = target.Width; Height = target.Height;
            Left = target.Left; Top = target.Top;
            return;
        }

        _windowAnimation?.Stop();

        var storyboard = new Storyboard();
        void Add(string property, double to)
        {
            var animation = new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(300)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTargetProperty(animation, new PropertyPath(property));
            storyboard.Children.Add(animation);
        }
        Add("Width", target.Width);
        Add("Height", target.Height);
        Add("Left", target.Left);
        Add("Top", target.Top);

        storyboard.Completed += (_, _) =>
        {
            // Lock in the exact final values (clean state for later drag/resize).
            Width = target.Width; Height = target.Height;
            Left = target.Left; Top = target.Top;
        };
        _windowAnimation = storyboard;
        storyboard.Begin(this, true);
    }

    // ---------- keep the compact overlay fully inside the work area ----------

    private void OnWindowLocationChanged(object? sender, EventArgs e)
    {
        if (_viewModel.Overlay.Mode != OverlayMode.Interview)
            return;
        var clamped = _viewModel.Overlay.ClampPosition((int)Left, (int)Top, (int)Width, (int)Height);
        if (clamped.Left != (int)Left || clamped.Top != (int)Top)
        {
            Left = clamped.Left;
            Top = clamped.Top;
        }
    }

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_viewModel.Overlay.Mode != OverlayMode.Interview)
            return;
        var (w, h) = _viewModel.Overlay.ClampSize((int)Width, (int)Height);
        if (w != (int)Width || h != (int)Height)
        {
            Width = w;
            Height = h;
        }
    }

    /// <summary>The compact header is a dedicated drag area (native title bar still works).</summary>
    private void OnInterviewHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _windowAnimation?.Stop(); // never fight a running transition
            DragMove();
        }
    }

    // ---------- subtle one-shot entrance for NEW content (interview mode only) ----------

    private static readonly HashSet<string> ContentProperties = new()
    {
        nameof(MainViewModel.TranscriptText),
        nameof(MainViewModel.TranslationText),
        nameof(MainViewModel.AnswerText)
    };

    private readonly Dictionary<string, string> _lastAnimatedContent = new();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        string? name = e.PropertyName;
        if (name is null || !ContentProperties.Contains(name))
            return;
        if (_viewModel.Overlay.Mode != OverlayMode.Interview || !_viewModel.AnimationsEnabled)
            return;

        string text = name switch
        {
            nameof(MainViewModel.TranscriptText) => _viewModel.TranscriptText ?? string.Empty,
            nameof(MainViewModel.TranslationText) => _viewModel.TranslationText ?? string.Empty,
            _ => _viewModel.AnswerText ?? string.Empty
        };
        if (string.IsNullOrEmpty(text) || _lastAnimatedContent.GetValueOrDefault(name) == text)
        {
            _lastAnimatedContent[name] = text;
            return;
        }
        _lastAnimatedContent[name] = text;

        FrameworkElement panel = name switch
        {
            nameof(MainViewModel.TranscriptText) => SpeechPanel,
            nameof(MainViewModel.TranslationText) => TranslationPanel,
            _ => AnswerPanel
        };
        AnimateContentEntrance(panel);
    }

    /// <summary>
    /// A cheap one-shot fade + 3px rise when new content arrives. Never moves
    /// the window itself, never loops, and is skipped entirely under the
    /// reduced-motion preference.
    /// </summary>
    private static void AnimateContentEntrance(FrameworkElement panel)
    {
        var transform = panel.RenderTransform as TranslateTransform ?? new TranslateTransform();
        panel.RenderTransform = transform;

        var opacity = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(opacity, panel);
        Storyboard.SetTargetProperty(opacity, new PropertyPath(OpacityProperty));

        var rise = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(rise, transform);
        Storyboard.SetTargetProperty(rise, new PropertyPath(TranslateTransform.YProperty));

        var storyboard = new Storyboard();
        storyboard.Children.Add(opacity);
        storyboard.Children.Add(rise);
        storyboard.Completed += (_, _) =>
        {
            panel.Opacity = 1.0;
            transform.Y = 0;
        };

        panel.Opacity = 0.45;
        transform.Y = 3;
        storyboard.Begin();
    }
}
