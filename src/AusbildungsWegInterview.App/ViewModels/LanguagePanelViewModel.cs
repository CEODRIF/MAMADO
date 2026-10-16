using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using AusbildungsWegInterview.App;
using AusbildungsWegInterview.Core.Answers;
using AusbildungsWegInterview.Core.Common;
using AusbildungsWegInterview.Core.Pipeline;
using AusbildungsWegInterview.Core.Translation;
using AusbildungsWegInterview.Localization;

namespace AusbildungsWegInterview.App.ViewModels;

/// <summary>
/// One language panel of the detected question or the suggested answer (the
/// mockup's Original / Translation (English) / Answer (Arabic) tiles).
///
/// Holds the RAW pipeline data (text + machine status codes) and re-renders the
/// localized caption/state whenever the UI language changes — the same panel
/// therefore never shows stale or raw-English text.
/// </summary>
public sealed class LanguagePanelViewModel : INotifyPropertyChanged
{
    private readonly string _captionKey;
    private string _text = string.Empty;
    private PanelState _state = PanelState.Pending;
    private TranslationStatus? _translationStatus;
    private string? _statusMessage;
    private AnswerStatus? _answerStatus;
    private string? _model;
    private string _caption = string.Empty;
    private string _stateText = string.Empty;

    public LanguagePanelViewModel(string language, string captionKey, bool isPrimary)
    {
        Language = language;
        _captionKey = captionKey;
        IsPrimary = isPrimary;
        CopyCommand = new RelayCommand(_ => Copy());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when the clipboard refused the copy (locked by another app).</summary>
    public event EventHandler? CopyFailed;

    public string Language { get; }
    public bool IsPrimary { get; }

    /// <summary>Source language of the panel content (needed to localize pair-specific statuses).</summary>
    public string? SourceLanguage { get; set; }

    /// <summary>Localized caption, e.g. "Translation (English)".</summary>
    public string Caption => _caption;

    public string Text => _text;

    /// <summary>Localized status for pending / unavailable panels (empty when ready).</summary>
    public string StateText => _stateText;

    public bool HasStateText => !string.IsNullOrEmpty(_stateText);
    public bool IsPending => _state == PanelState.Pending;
    public bool IsUnavailable => _state == PanelState.Unavailable;
    public bool IsReady => _state == PanelState.Ready;
    public bool CanCopy => !string.IsNullOrWhiteSpace(_text);

    public RelayCommand CopyCommand { get; }

    /// <summary>Question/answer text produced by the pipeline (raw, never localized).</summary>
    public void SetContent(
        string? text,
        PanelState state,
        TranslationStatus? translationStatus = null,
        string? statusMessage = null,
        AnswerStatus? answerStatus = null,
        string? model = null)
    {
        _text = text ?? string.Empty;
        _state = state;
        _translationStatus = translationStatus;
        _statusMessage = statusMessage;
        _answerStatus = answerStatus;
        _model = model;
        Raise(nameof(Text));
        Raise(nameof(CanCopy));
    }

    /// <summary>(Re)applies localized caption + status text (called on language change).</summary>
    public void ApplyLocalization(LocalizationManager loc)
    {
        _caption = $"{loc[_captionKey]} ({LanguageNames.DisplayName(Language)})";
        _stateText = ResolveStateText(loc);
        Raise(nameof(Caption));
        Raise(nameof(StateText));
        Raise(nameof(HasStateText));
        Raise(nameof(IsPending));
        Raise(nameof(IsUnavailable));
        Raise(nameof(IsReady));
        CopyCommand.RaiseCanExecuteChanged();
    }

    private string ResolveStateText(LocalizationManager loc)
    {
        if (_state == PanelState.Ready)
            return string.Empty;

        if (_state == PanelState.Pending)
            return loc["PanelPending"];

        // Unavailable: prefer the generation status (primary answer panel), then
        // the translation status (translated panel). Both are machine codes that
        // resolve to localized text — never the raw provider message.
        if (_answerStatus is { } answerStatus && answerStatus != AnswerStatus.None)
            return AnswerStatusLocalizer.Localize(loc, answerStatus, _model);

        if (_translationStatus is { } translationStatus)
            return TranslationStatusLocalizer.Localize(loc, translationStatus, SourceLanguage, Language);

        if (!string.IsNullOrWhiteSpace(_statusMessage))
            return loc["PanelUnavailable"];

        return string.Empty;
    }

    private void Copy()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_text))
                Clipboard.SetText(_text);
        }
        catch
        {
            CopyFailed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Raise(string name)
    {
        if (PropertyChanged is null)
            return;
        PropertyChanged(this, new PropertyChangedEventArgs(name));
    }
}
