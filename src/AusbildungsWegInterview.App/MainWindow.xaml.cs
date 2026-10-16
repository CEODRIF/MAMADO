using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using AusbildungsWegInterview.App.ViewModels;

namespace AusbildungsWegInterview.App;

/// <summary>
/// The single window. Code-behind stays deliberately minimal (MVVM): it only
/// constructs the view model, sets the DataContext, opens the "Add document"
/// menu (a pure view concern — every menu entry runs a real view-model command),
/// and raises screen-reader live-region announcements on real content changes.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(AppServices services)
    {
        InitializeComponent();
        _viewModel = new MainViewModel(services, Dispatcher);
        DataContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>
    /// Opens the CV / job-description / notes menu of the Document Context panel.
    /// The entries are bound to the same commands used on the Documents page.
    /// </summary>
    private void AddDocument_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is not { } menu)
            return;

        menu.PlacementTarget = button;
        menu.DataContext = DataContext;
        menu.IsOpen = true;
    }

    // ---------- screen-reader live-region announcements ----------

    private static readonly HashSet<string> AnnouncedProperties = new()
    {
        nameof(MainViewModel.TranscriptText),
        nameof(MainViewModel.TranslationText),
        nameof(MainViewModel.AnswerText)
    };

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        string? name = e.PropertyName;
        if (name is null || !AnnouncedProperties.Contains(name))
            return;

        AnnounceContent(name);
    }

    /// <summary>
    /// Notifies assistive technologies that a live content area has new text.
    /// LiveRegionChanged is WPF's built-in "content updated" automation event
    /// (no external dependency). Accessibility must never break the UI: all
    /// failures are silently ignored.
    /// </summary>
    private void AnnounceContent(string propertyName)
    {
        try
        {
            FrameworkElement target = propertyName switch
            {
                nameof(MainViewModel.TranscriptText) => MainSpeechText,
                nameof(MainViewModel.TranslationText) => MainTranslationText,
                _ => MainAnswerText
            };

            var peer = UIElementAutomationPeer.FromElement(target)
                       ?? UIElementAutomationPeer.CreatePeerForElement(target);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        catch
        {
            // Accessibility must never break the UI.
        }
    }
}
