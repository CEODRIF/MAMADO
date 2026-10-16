using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using AusbildungsWegInterview.Core.Answers;
using AusbildungsWegInterview.Core.Audio;
using AusbildungsWegInterview.Core.Common;
using AusbildungsWegInterview.Core.Config;
using AusbildungsWegInterview.Core.Pipeline;
using AusbildungsWegInterview.Core.Speech;
using AusbildungsWegInterview.Core.Translation;
using AusbildungsWegInterview.Infrastructure.Documents;
using AusbildungsWegInterview.Localization;

namespace AusbildungsWegInterview.App.ViewModels;

/// <summary>Combo-box option with a language-independent value + a localized display.</summary>
public sealed class PickerOption
{
    public PickerOption(string value, string display) { Value = value; Display = display; }
    public string Value { get; }
    public string Display { get; }
    public override string ToString() => Display;
}

/// <summary>
/// Main floating-window view model. Binds to the InterviewPipeline and exposes
/// UI state + commands. All provider access is through abstractions (the VM never
/// talks to a specific AI runtime directly).
///
/// Phase 2 additions:
///  * Detected-question panel (last question that produced a suggestion).
///  * Explicit "system audio capture active" indicator (loopback, no microphone).
///  * Honest translation status (translated / pass-through / provider missing).
///  * Whisper.cpp executable + model path settings (applied via pipeline rebuild).
///  * Translation on/off toggle and answer style (concise / natural / professional).
///  * Ollama model availability + setup guidance; conservative model suggestions
///    based on detected RAM.
///  * Duplicate-operation guard (no concurrent start/stop/settings-rebuild).
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Dispatcher _dispatcher;

    // ---- collections for pickers ----
    public ObservableCollection<AudioDevice> OutputDevices { get; } = new();
    public ObservableCollection<string> SourceLanguages { get; } = new() { "auto", "de", "en", "fr", "ar", "es", "it", "nl", "tr" };
    public ObservableCollection<string> TargetLanguages { get; } = new() { "en", "de", "fr", "ar", "es", "it", "nl", "tr" };
    public ObservableCollection<string> AnswerLanguages { get; } = new() { "de", "en", "fr", "ar" };
    // Localized picker options (display rebuilt on UI-language change; values are enum names).
    public ObservableCollection<PickerOption> AnswerLengthOptions { get; } = new();
    public ObservableCollection<PickerOption> AnswerStyleOptions { get; } = new();
    public ObservableCollection<PickerOption> TranslationEngineOptions { get; } = new();

    /// <summary>UI interface language (independent of interview/translation/answer languages). Native names.</summary>
    public ObservableCollection<PickerOption> UiLanguageOptions { get; } = new();
    public ObservableCollection<string> CefrLevelOptions { get; } = new() { "A1", "A2", "B1", "B2", "C1", "C2" };
    public ObservableCollection<string> WhisperModelOptions { get; } = new() { "tiny", "base", "small", "medium" };

    // ---- settings-backed (persisted to config) ----
    private AudioDevice? _selectedOutputDevice;
    public AudioDevice? SelectedOutputDevice
    {
        get => _selectedOutputDevice;
        set { if (Set(ref _selectedOutputDevice, value)) { _services.Config.OutputDeviceId = value?.Id; SaveConfig(); } }
    }

    private string _selectedSourceLanguage = "auto";
    public string SelectedSourceLanguage
    {
        get => _selectedSourceLanguage;
        set { if (Set(ref _selectedSourceLanguage, value)) { _services.Config.SourceLanguage = value == "auto" ? null : value; SaveConfig(); } }
    }

    private string _selectedTargetLanguage = "en";
    public string SelectedTargetLanguage
    {
        get => _selectedTargetLanguage;
        set { if (Set(ref _selectedTargetLanguage, value)) { _services.Config.TranslationLanguage = value; SaveConfig(); } }
    }

    private bool _translationEnabled = true;
    public bool TranslationEnabled
    {
        get => _translationEnabled;
        set
        {
            if (!Set(ref _translationEnabled, value)) return;
            _services.Config.TranslationEnabled = value;
            ApplySpeechSettings();
        }
    }

    private PickerOption? _selectedTranslationEngine;
    public PickerOption? SelectedTranslationEngine
    {
        get => _selectedTranslationEngine;
        set
        {
            if (!Set(ref _selectedTranslationEngine, value)) return;
            _services.Config.TranslationProvider = value?.Value switch
            {
                "WhisperTranslate" => TranslationProvider.WhisperTranslate,
                "ExternalCli" => TranslationProvider.ExternalCli,
                _ => TranslationProvider.Ollama
            };
            ApplySpeechSettings();
        }
    }

    private string _translatorCliPath = string.Empty;
    public string TranslatorCliPath
    {
        get => _translatorCliPath;
        set { if (Set(ref _translatorCliPath, value)) { _services.Config.TranslatorCliPath = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); SaveConfig(); ApplySpeechSettings(); } }
    }

    private string _translatorCliArgs = string.Empty;
    public string TranslatorCliArgs
    {
        get => _translatorCliArgs;
        set { if (Set(ref _translatorCliArgs, value)) { _services.Config.TranslatorCliArgsTemplate = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); SaveConfig(); ApplySpeechSettings(); } }
    }

    private string _selectedAnswerLanguage = "de";
    public string SelectedAnswerLanguage
    {
        get => _selectedAnswerLanguage;
        set { if (Set(ref _selectedAnswerLanguage, value)) { _services.Config.AnswerLanguage = value; SaveConfig(); } }
    }

    private PickerOption? _selectedAnswerLength;
    public PickerOption? SelectedAnswerLength
    {
        get => _selectedAnswerLength;
        set { if (Set(ref _selectedAnswerLength, value)) { _services.Config.AnswerLength = value?.Value switch { "Concise" => AnswerLength.Concise, "Detailed" => AnswerLength.Detailed, _ => AnswerLength.Normal }; SaveConfig(); } }
    }

    private PickerOption? _selectedAnswerStyle;
    public PickerOption? SelectedAnswerStyle
    {
        get => _selectedAnswerStyle;
        set { if (Set(ref _selectedAnswerStyle, value)) { _services.Config.AnswerStyle = value?.Value switch { "Concise" => AnswerStyle.Concise, "Professional" => AnswerStyle.Professional, _ => AnswerStyle.Natural }; SaveConfig(); } }
    }

    // ---- UI language + theme (independent of the interview/translation/answer languages) ----
    private PickerOption? _selectedUiLanguage;
    public PickerOption? SelectedUiLanguage
    {
        get => _selectedUiLanguage;
        set
        {
            if (!Set(ref _selectedUiLanguage, value)) return;
            if (value is null || !UiLanguages.IsValid(value.Value)) return;
            _services.Config.UiLanguage = value.Value;
            SaveConfig();
            Loc.SetCulture(value.Value);
            OnLanguageChanged();
        }
    }

    private PickerOption? _selectedTheme;
    public PickerOption? SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (!Set(ref _selectedTheme, value)) return;
            if (value is null) return;
            _services.Config.Theme = value.Value;
            SaveConfig();
            ThemeApplier.Apply(value.Value);
            OnPropertyChanged(nameof(ThemeIcon));
        }
    }

    /// <summary>Header glyph: shows what the toggle will switch TO (moon = to dark, sun = to light).</summary>
    public string ThemeIcon => SelectedTheme?.Value == ThemeApplier.Dark ? "\u2600" : "\u263E";

    private string _modelSuggestedText = string.Empty;
    /// <summary>Localized, pre-formatted model suggestion line (Advanced section).</summary>
    public string ModelSuggestedText { get => _modelSuggestedText; private set => Set(ref _modelSuggestedText, value); }

    /// <summary>Toggles between light and dark theme (accessible header control).</summary>
    public void ToggleTheme()
    {
        var next = SelectedTheme?.Value == ThemeApplier.Dark ? ThemeApplier.Light : ThemeApplier.Dark;
        SelectedTheme = ThemeOptions.FirstOrDefault(o => o.Value == next);
    }

    /// <summary>Localized theme names (rebuilt on UI-language change).</summary>
    public ObservableCollection<PickerOption> ThemeOptions { get; } = new();

    public bool IsRtl { get; private set; }

    /// <summary>String indexer for XAML: {Binding T['Key']} - re-evaluates when the UI language changes.</summary>
    public LocalizationManager T => Loc;

    /// <summary>Runtime UI language manager (static catalogs, no machine translation).</summary>
    public LocalizationManager Loc { get; }

    /// <summary>CEFR level (A1–C2) for suggested answers. Applied per request; no pipeline rebuild needed.</summary>
    private string _selectedCefrLevel = "B1";
    public string SelectedCefrLevel
    {
        get => _selectedCefrLevel;
        set
        {
            if (!Set(ref _selectedCefrLevel, value)) return;
            if (Enum.TryParse<CefrLevel>(value, out var level))
            {
                _services.Config.AnswerLevel = level;
                SaveConfig();
            }
        }
    }

    private string _ollamaModel = "llama3.1:8b";
    public string OllamaModel
    {
        get => _ollamaModel;
        set
        {
            if (!Set(ref _ollamaModel, value)) return;
            var v = value?.Trim();
            _services.Config.OllamaModel = string.IsNullOrEmpty(v) ? "llama3.1:8b" : v;
            SaveConfig();
            ApplySpeechSettings(); // generator holds the model name from build time
        }
    }

    private string _selectedWhisperModel = "base";
    public string SelectedWhisperModel
    {
        get => _selectedWhisperModel;
        set { if (Set(ref _selectedWhisperModel, value)) { _services.Config.WhisperModel = value; SaveConfig(); ApplySpeechSettings(); } }
    }

    private string _whisperExecutablePath = string.Empty;
    public string WhisperExecutablePath
    {
        get => _whisperExecutablePath;
        set { if (Set(ref _whisperExecutablePath, value)) { _services.Config.WhisperExecutablePath = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); SaveConfig(); ApplySpeechSettings(); } }
    }

    private string _whisperModelPath = string.Empty;
    public string WhisperModelPath
    {
        get => _whisperModelPath;
        set { if (Set(ref _whisperModelPath, value)) { _services.Config.WhisperModelPath = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); SaveConfig(); ApplySpeechSettings(); } }
    }

    private bool _alwaysOnTop;
    public bool AlwaysOnTop
    {
        get => _alwaysOnTop;
        set { if (Set(ref _alwaysOnTop, value)) { _services.Config.AlwaysOnTop = value; SaveConfig(); } }
    }

    // ---- display state (private set + auto-notify) ----
    // (initial values are localized in the constructor via ApplyLocalizedInitialStrings)
    private string _statusText = string.Empty;
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    private bool _isListening;
    public bool IsListening { get => _isListening; private set => Set(ref _isListening, value); }

    /// <summary>True while system output audio (loopback) is actually being captured.</summary>
    public bool IsCapturingSystemAudio => IsListening;

    private string _transcriptText = string.Empty;
    public string TranscriptText { get => _transcriptText; private set => Set(ref _transcriptText, value); }

    private bool _isTranscriptProvisional;
    public bool IsTranscriptProvisional { get => _isTranscriptProvisional; private set => Set(ref _isTranscriptProvisional, value); }

    private string _translationText = string.Empty;
    public string TranslationText { get => _translationText; private set => Set(ref _translationText, value); }

    private string _translationStatusText = string.Empty;
    public string TranslationStatusText { get => _translationStatusText; private set => Set(ref _translationStatusText, value); }

    private string _detectedQuestionText = string.Empty;
    public string DetectedQuestionText
    {
        get => _detectedQuestionText;
        private set
        {
            Set(ref _detectedQuestionText, value);
            OnPropertyChanged(nameof(HasDetectedQuestion));
            RaiseAnswerCommandStates();
        }
    }
    public bool HasDetectedQuestion => !string.IsNullOrEmpty(DetectedQuestionText);

    private string _answerText = string.Empty;
    public string AnswerText { get => _answerText; private set => Set(ref _answerText, value); }

    private bool _isAnswerProvisional;
    public bool IsAnswerProvisional { get => _isAnswerProvisional; private set => Set(ref _isAnswerProvisional, value); }

    /// <summary>True while the panel shows a REAL model suggestion (enables Copy + the review disclaimer).</summary>
    private bool _answerIsSuggestion;
    public bool AnswerIsSuggestion { get => _answerIsSuggestion; private set => Set(ref _answerIsSuggestion, value); }

    private string _errorText = string.Empty;
    public string ErrorText
    {
        get => _errorText;
        private set { Set(ref _errorText, value); OnPropertyChanged(nameof(HasError)); }
    }
    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    // ---- source-of-truth for LOCALIZED dynamic strings ----
    // These are re-localized on UI-language change (OnLanguageChanged) so no
    // stale translation survives a language switch.
    private Exception? _lastError;
    private string? _lastErrorKey;
    private object?[]? _lastErrorArgs;
    private TranslationResult? _lastTranslationResult;
    private AnswerSuggestion? _lastAnswerSuggestion;

    /// <summary>Shows a runtime error by its category (localized); raw exception text is never displayed.</summary>
    private void ShowRuntimeError(Exception ex)
    {
        _lastError = ex;
        _lastErrorKey = null;
        _lastErrorArgs = null;
        ErrorText = SpeechErrorLocalizer.Localize(T, ex);
    }

    /// <summary>Shows a keyed UI error (optionally formatted) and remembers it for re-localization.</summary>
    private void ShowKeyedError(string key, params object?[] args)
    {
        _lastError = null;
        _lastErrorKey = key;
        _lastErrorArgs = args;
        ErrorText = args is { Length: > 0 } ? T.F(key, args) : T[key];
    }

    private void ClearErrorState()
    {
        _lastError = null;
        _lastErrorKey = null;
        _lastErrorArgs = null;
        ErrorText = string.Empty;
    }

    /// <summary>Re-localizes every dynamic, language-dependent string after a UI language change.</summary>
    private void ReLocalizeDynamicStrings()
    {
        if (_lastError is not null)
            ErrorText = SpeechErrorLocalizer.Localize(T, _lastError);
        else if (_lastErrorKey is not null)
            ErrorText = _lastErrorArgs is { Length: > 0 } ? T.F(_lastErrorKey, _lastErrorArgs) : T[_lastErrorKey];

        TranslationStatusText = _lastTranslationResult is not null && !_lastTranslationResult.IsTranslated
            ? TranslationStatusLocalizer.Localize(T, _lastTranslationResult)
            : string.Empty;

        if (_lastAnswerSuggestion is { Status: not AnswerStatus.None })
            AnswerText = AnswerStatusLocalizer.Localize(T, _lastAnswerSuggestion);
    }

    private string _aiStatusText = string.Empty;
    public string AiStatusText { get => _aiStatusText; private set => Set(ref _aiStatusText, value); }

    private string _speechSetupStatus = string.Empty;
    public string SpeechSetupStatus { get => _speechSetupStatus; private set => Set(ref _speechSetupStatus, value); }

    private string _contextStatus = string.Empty;
    public string ContextStatus { get => _contextStatus; private set => Set(ref _contextStatus, value); }

    // ---- CV / job description documents (Phase 3) ----
    // Review-before-use, memory-only. Imported text is grounded for answers
    // ONLY after the user reviews it in the box and presses "Use". Nothing is
    // persisted; the pipeline context (and thus the answer prompt) holds the
    // reviewed text in memory.
    private readonly ReviewableDocument _cvDoc = new();
    private readonly ReviewableDocument _jdDoc = new();
    private readonly ReviewableDocument _notesDoc = new();
    private readonly DocumentImporter _docImporter = new();

    private string _cvStateText = string.Empty;
    public string CvStateText { get => _cvStateText; private set => Set(ref _cvStateText, value); }

    private string _jdStateText = string.Empty;
    public string JdStateText { get => _jdStateText; private set => Set(ref _jdStateText, value); }

    private string _notesStateText = string.Empty;
    public string NotesStateText { get => _notesStateText; private set => Set(ref _notesStateText, value); }

    /// <summary>CV review box (two-way): shows imported text for review; edits re-apply live while in use.</summary>
    public string CvText
    {
        get => _cvDoc.Text;
        set
        {
            _cvDoc.Edit(value);
            ApplyDocumentToContext(_cvDoc, "cv");
            UpdateDocState(_cvDoc, "cv");
            RaiseDocCommandStates();
        }
    }

    /// <summary>Job-description review box: paste directly, or import a file.</summary>
    public string JdText
    {
        get => _jdDoc.Text;
        set
        {
            _jdDoc.Edit(value);
            ApplyDocumentToContext(_jdDoc, "jd");
            UpdateDocState(_jdDoc, "jd");
            RaiseDocCommandStates();
        }
    }

    /// <summary>Interview notes / preparation sheet review box (mockup: notes slot).</summary>
    public string NotesText
    {
        get => _notesDoc.Text;
        set
        {
            _notesDoc.Edit(value);
            ApplyDocumentToContext(_notesDoc, "notes");
            UpdateDocState(_notesDoc, "notes");
            RaiseDocCommandStates();
        }
    }

    private void RaiseDocCommandStates()
    {
        ClearCvCommand?.RaiseCanExecuteChanged();
        ClearJdCommand?.RaiseCanExecuteChanged();
        ClearNotesCommand?.RaiseCanExecuteChanged();
    }

    /// <summary>Chip ✕ handler: removes exactly the personal data of that slot.</summary>
    private bool RemoveDocumentBySlot(string slot)
    {
        switch (slot)
        {
            case "cv": ClearDocument(_cvDoc, "cv"); return true;
            case "jd": ClearDocument(_jdDoc, "jd"); return true;
            case "notes": ClearDocument(_notesDoc, "notes"); return true;
            default: return false;
        }
    }

    /// <summary>Pushes the document's ACTIVE (reviewed) text into the answer context.</summary>
    private void ApplyDocumentToContext(ReviewableDocument doc, string slot)
    {
        string? active = doc.IsInUse ? (string.IsNullOrWhiteSpace(doc.ActiveText) ? null : doc.ActiveText) : null;
        _services.Pipeline.Context = slot switch
        {
            "cv" => _services.Pipeline.Context.WithCv(active),
            "jd" => _services.Pipeline.Context.WithJobDescription(active),
            _ => _services.Pipeline.Context.WithNotes(active)
        };
        RecomputeContextStatus();
    }

    /// <summary>Localized per-document status line (not imported / review / in use).</summary>
    private void UpdateDocState(ReviewableDocument doc, string slot)
    {
        string text = doc.State switch
        {
            DocumentState.InUse => T.F("DocInUse", doc.ActiveText?.Length ?? 0),
            DocumentState.Review => T["DocReview"],
            _ => T["DocNotImported"]
        };
        switch (slot)
        {
            case "cv": CvStateText = text; break;
            case "jd": JdStateText = text; break;
            default: NotesStateText = text; break;
        }
    }

    private void UpdateAllDocStates()
    {
        UpdateDocState(_cvDoc, "cv");
        UpdateDocState(_jdDoc, "jd");
        UpdateDocState(_notesDoc, "notes");
        CvChip.ApplyLocalization(T);
        JdChip.ApplyLocalization(T);
        NotesChip.ApplyLocalization(T);
    }

    private string DocFilter
        => $"{T["FileFilterPdf"]} | *.pdf|{T["FileFilterDocx"]} | *.docx|{T["FileFilterText"]} | *.txt;*.md|{T["FileFilterAll"]} | *.*";

    private void ShowDocError(DocumentImportError error)
    {
        if (error is DocumentImportError.Oversized)
        {
            ShowKeyedError("ErrorDocOversized", _docImporter.MaxFileBytes / (1024 * 1024));
            return;
        }
        ShowKeyedError(error switch
        {
            DocumentImportError.Unsupported => "ErrorDocUnsupported",
            DocumentImportError.Empty => "ErrorDocEmpty",
            DocumentImportError.NoText => "ErrorDocNoText",
            _ => "ErrorDocCorrupt"
        });
    }

    // Conservative, RAM-based model suggestions (informational; nothing is downloaded automatically).
    public string SuggestedOllamaModel { get; }
    public string SuggestedWhisperModel { get; }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set { Set(ref _isBusy, value); RaiseCommandStates(); } }

    // Brand status colors (ausbildungsweg.net palette): success / warning / danger / faint.
    private static readonly Brush StatusListeningBrush = Brush("#1F9D72");
    private static readonly Brush StatusPreparingBrush = Brush("#A3611C");
    private static readonly Brush StatusFaultBrush = Brush("#D9535D");
    private static readonly Brush StatusIdleBrush = Brush("#9598B6");

    private Brush _statusBrush = StatusIdleBrush;
    public Brush StatusBrush { get => _statusBrush; private set => Set(ref _statusBrush, value); }

    private static Brush Brush(string hex)
        => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    // ===================== MAMADO shell (mockup layout) =====================

    // ---- left navigation rail (mockup: 3 main entries, 2 at the bottom) ----
    public ObservableCollection<NavigationItem> NavItems { get; } = new();
    public ObservableCollection<NavigationItem> NavPrimary { get; } = new();
    public ObservableCollection<NavigationItem> NavSecondary { get; } = new();

    private bool _isGenerating;
    public bool IsGenerating { get => _isGenerating; private set => Set(ref _isGenerating, value); }

    private NavigationItem? _selectedNav;
    public NavigationItem? SelectedNav
    {
        get => _selectedNav;
        set
        {
            if (!Set(ref _selectedNav, value))
                return;
            OnPropertyChanged(nameof(IsInterviewPage));
            OnPropertyChanged(nameof(IsDocumentsPage));
            OnPropertyChanged(nameof(IsSettingsPage));
            OnPropertyChanged(nameof(IsLogsPage));
            OnPropertyChanged(nameof(IsFeedbackPage));
            if (IsLogsPage)
                _ = RefreshLogsAsync();
        }
    }

    public bool IsInterviewPage => _selectedNav?.Key == "interview";
    public bool IsDocumentsPage => _selectedNav?.Key == "documents";
    public bool IsSettingsPage => _selectedNav?.Key == "settings";
    public bool IsLogsPage => _selectedNav?.Key == "logs";
    public bool IsFeedbackPage => _selectedNav?.Key == "feedback";

    // ---- top status pills: driven ONLY by real health checks ----
    private string _whisperPillState = string.Empty;
    public string WhisperPillState { get => _whisperPillState; private set => Set(ref _whisperPillState, value); }

    private bool _whisperPillReady;
    public bool WhisperPillReady { get => _whisperPillReady; private set => Set(ref _whisperPillReady, value); }

    private bool _whisperPillFaulted;
    public bool WhisperPillFaulted { get => _whisperPillFaulted; private set => Set(ref _whisperPillFaulted, value); }

    private string _ollamaPillState = string.Empty;
    public string OllamaPillState { get => _ollamaPillState; private set => Set(ref _ollamaPillState, value); }

    private bool _ollamaPillReady;
    public bool OllamaPillReady { get => _ollamaPillReady; private set => Set(ref _ollamaPillReady, value); }

    private bool _ollamaPillFaulted;
    public bool OllamaPillFaulted { get => _ollamaPillFaulted; private set => Set(ref _ollamaPillFaulted, value); }

    private string _translationPillState = string.Empty;
    public string TranslationPillState { get => _translationPillState; private set => Set(ref _translationPillState, value); }

    private bool _translationPillReady;
    public bool TranslationPillReady { get => _translationPillReady; private set => Set(ref _translationPillReady, value); }

    private bool _translationPillFaulted;
    public bool TranslationPillFaulted { get => _translationPillFaulted; private set => Set(ref _translationPillFaulted, value); }

    // ---- listening panel waveform (real capture levels) ----
    private const int WaveformCapacity = 72;
    public ObservableCollection<double> WaveformLevels { get; } = new();
    public bool HasWaveform => WaveformLevels.Count > 0;

    // ---- question / answer language panels (mockup trios) ----
    public ObservableCollection<LanguagePanelViewModel> QuestionPanels { get; } = new();
    public ObservableCollection<LanguagePanelViewModel> AnswerPanels { get; } = new();
    private string? _questionSourceLanguage;

    // ---- document context chips (real file name + real metadata) ----
    public DocumentChipViewModel CvChip { get; }
    public DocumentChipViewModel JdChip { get; }
    public DocumentChipViewModel NotesChip { get; }

    // ---- local log viewer (no interview content is ever written to it) ----
    public ObservableCollection<string> LogLines { get; } = new();
    public bool HasLogLines => LogLines.Count > 0;
    private bool _logsUnavailable;
    public bool LogsUnavailable { get => _logsUnavailable; private set => Set(ref _logsUnavailable, value); }

    // ---- about / feedback page ----
    public string AppVersion { get; }

    private string _appVersionText = string.Empty;
    public string AppVersionText { get => _appVersionText; private set => Set(ref _appVersionText, value); }

    // ---- commands ----
    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand RefreshDevicesCommand { get; }
    public RelayCommand DownloadModelCommand { get; }
    public RelayCommand LoadCvCommand { get; }
    public RelayCommand UseCvCommand { get; }
    public RelayCommand ClearCvCommand { get; }
    public RelayCommand LoadJdCommand { get; }
    public RelayCommand UseJdCommand { get; }
    public RelayCommand ClearJdCommand { get; }
    public RelayCommand CopyAnswerCommand { get; }
    public RelayCommand LoadNotesCommand { get; }
    public RelayCommand UseNotesCommand { get; }
    public RelayCommand ClearNotesCommand { get; }
    public RelayCommand ShorterCommand { get; }
    public RelayCommand LongerCommand { get; }
    public RelayCommand RegenerateCommand { get; }
    public RelayCommand RefreshLogsCommand { get; }
    public RelayCommand SelectPageCommand { get; }
    public RelayCommand SetAnswerLengthCommand { get; }
    public RelayCommand CopyDiagnosticsCommand { get; }
    public RelayCommand ClearErrorCommand { get; }
    public RelayCommand RefreshStatusCommand { get; }
    public RelayCommand ToggleThemeCommand { get; }

    // ---- pipeline event wiring (unsubscribed on rebuild) ----
    private EventHandler<TranscriptUpdatedEventArgs>? _hTranscript;
    private EventHandler<TranslationUpdatedEventArgs>? _hTranslation;
    private EventHandler<AnswerUpdatedEventArgs>? _hAnswer;
    private EventHandler? _hGenerating;
    private EventHandler<QuestionSetUpdatedEventArgs>? _hQuestionSet;
    private EventHandler<AnswersUpdatedEventArgs>? _hAnswers;
    private EventHandler<AudioLevelUpdatedEventArgs>? _hLevel;
    private EventHandler<PipelineStateChangedEventArgs>? _hState;
    private EventHandler<CaptureErrorEventArgs>? _hError;
    private InterviewPipeline? _wiredPipeline;

    public MainViewModel(AppServices services, Dispatcher dispatcher)
    {
        _services = services;
        _dispatcher = dispatcher;

        // UI language + theme are restored from the persisted configuration.
        Loc = new LocalizationManager(_services.Config.UiLanguage);
        Loc.LanguageChanged += (_, _) => OnLanguageChanged();
        IsRtl = Loc.IsRtl;

        foreach (var l in UiLanguages.All)
            UiLanguageOptions.Add(new PickerOption(l.Code, l.NativeName));
        BuildThemeOptions();
        BuildLocalizedPickerOptions();
        _selectedUiLanguage = UiLanguageOptions.FirstOrDefault(o => o.Value == _services.Config.UiLanguage);
        _selectedTheme = ThemeOptions.FirstOrDefault(o => o.Value == _services.Config.Theme);

        StartCommand = new RelayCommand(_ => _ = StartAsync(), _ => !IsListening && !IsBusy);
        StopCommand = new RelayCommand(_ => _ = StopAsync(), _ => IsListening && !IsBusy);
        RefreshDevicesCommand = new RelayCommand(_ => RefreshDevices(), _ => !IsBusy);
        DownloadModelCommand = new RelayCommand(_ => _ = DownloadModelAsync(), _ => !IsBusy);
        LoadCvCommand = new RelayCommand(_ => _ = ImportDocumentAsync(_cvDoc, "cv"));
        UseCvCommand = new RelayCommand(_ => UseDocument(_cvDoc, "cv"));
        ClearCvCommand = new RelayCommand(_ => ClearDocument(_cvDoc, "cv"), _ => !string.IsNullOrEmpty(_cvDoc.Text) || _cvDoc.IsInUse);
        LoadJdCommand = new RelayCommand(_ => _ = ImportDocumentAsync(_jdDoc, "jd"));
        UseJdCommand = new RelayCommand(_ => UseDocument(_jdDoc, "jd"));
        ClearJdCommand = new RelayCommand(_ => ClearDocument(_jdDoc, "jd"), _ => !string.IsNullOrEmpty(_jdDoc.Text) || _jdDoc.IsInUse);
        CopyAnswerCommand = new RelayCommand(_ => CopyAnswer(), _ => AnswerIsSuggestion && !IsBusy);
        LoadNotesCommand = new RelayCommand(_ => _ = ImportDocumentAsync(_notesDoc, "notes"));
        UseNotesCommand = new RelayCommand(_ => UseDocument(_notesDoc, "notes"));
        ClearNotesCommand = new RelayCommand(
            _ => ClearDocument(_notesDoc, "notes"),
            _ => !string.IsNullOrEmpty(_notesDoc.Text) || _notesDoc.IsInUse);

        // Shorter / Longer / Regenerate re-run the local model for the LAST
        // detected question (real generation, no simulated content).
        ShorterCommand = new RelayCommand(_ => _ = RegenerateAsync(shorter: true), _ => CanRegenerate);
        LongerCommand = new RelayCommand(_ => _ = RegenerateAsync(shorter: false), _ => CanRegenerate);
        RegenerateCommand = new RelayCommand(_ => _ = RegenerateAsync(length: _services.Config.AnswerLength), _ => CanRegenerate);

        RefreshLogsCommand = new RelayCommand(_ => _ = RefreshLogsAsync());
        // Shell shortcut buttons (header gear -> Settings, log icon -> Logs).
        SelectPageCommand = new RelayCommand(p =>
        {
            if (p is string key)
                SelectedNav = NavItems.FirstOrDefault(i => i.Key == key) ?? SelectedNav;
        });
        // Segmented length control (Short / Balanced / Long) of the answer panel.
        SetAnswerLengthCommand = new RelayCommand(p =>
        {
            if (p is string value)
                SelectedAnswerLength = AnswerLengthOptions.FirstOrDefault(o => o.Value == value) ?? SelectedAnswerLength;
        });
        CopyDiagnosticsCommand = new RelayCommand(_ => CopyDiagnostics());
        ClearErrorCommand = new RelayCommand(_ => ClearErrorState());
        RefreshStatusCommand = new RelayCommand(_ => _ = RefreshStatusAsync(), _ => !IsBusy);
        ToggleThemeCommand = new RelayCommand(_ => ToggleTheme());

        // ---- shell: navigation rail (Segoe MDL2 glyphs) ----
        NavItems.Add(new NavigationItem("interview", "NavInterview", "\uE720"));
        NavItems.Add(new NavigationItem("documents", "NavDocuments", "\uE8A5"));
        NavItems.Add(new NavigationItem("settings", "NavSettings", "\uE713"));
        NavItems.Add(new NavigationItem("logs", "NavLogs", "\uE81C"));
        NavItems.Add(new NavigationItem("feedback", "NavFeedback", "\uE939"));
        foreach (var item in NavItems)
            item.ApplyLocalization(Loc);
        foreach (var item in NavItems.Take(3))
            NavPrimary.Add(item);
        foreach (var item in NavItems.Skip(3))
            NavSecondary.Add(item);
        _selectedNav = NavItems[0];

        CvChip = new DocumentChipViewModel("cv", RemoveDocumentBySlot);
        JdChip = new DocumentChipViewModel("jd", RemoveDocumentBySlot);
        NotesChip = new DocumentChipViewModel("notes", RemoveDocumentBySlot);

        var assembly = typeof(MainViewModel).Assembly;
        AppVersion = assembly
            .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
            .OfType<AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";

        SuggestedOllamaModel = SafeSuggest(AusbildungsWegInterview.Infrastructure.SystemInfo.RecommendOllamaModel);
        SuggestedWhisperModel = SafeSuggest(AusbildungsWegInterview.Infrastructure.SystemInfo.RecommendWhisperModel);

        // OS-level device changes (default moved / device lost) are handled by the
        // capture; here we keep the device list + selection in sync and notify the user.
        _services.Audio.DeviceChanged += OnCaptureDeviceChanged;

        WirePipeline();
        RefreshDevices();
        ApplyConfigToUi();
        ApplyLocalizedInitialStrings();
        _ = RefreshStatusAsync();
    }

    /// <summary>Re-localizes the static parts of the UI without a restart (chrome re-binds via T, dynamic strings are recomputed).</summary>
    private void OnLanguageChanged()
    {
        IsRtl = Loc.IsRtl;
        OnPropertyChanged(nameof(IsRtl));

        // Rebuild picker displays; keep the current selections (by value).
        var engineValue = SelectedTranslationEngine?.Value;
        var lengthValue = SelectedAnswerLength?.Value;
        var styleValue = SelectedAnswerStyle?.Value;
        BuildLocalizedPickerOptions();
        _selectedTranslationEngine = TranslationEngineOptions.FirstOrDefault(o => o.Value == engineValue);
        _selectedAnswerLength = AnswerLengthOptions.FirstOrDefault(o => o.Value == lengthValue);
        _selectedAnswerStyle = AnswerStyleOptions.FirstOrDefault(o => o.Value == styleValue);
        BuildThemeOptions();
        _selectedTheme = ThemeOptions.FirstOrDefault(o => o.Value == _services.Config.Theme);

        // Re-localize dynamic strings.
        UpdateStatus(_services.Pipeline.State);
        UpdateSpeechSetupStatus();
        RecomputeContextStatus();
        UpdateAllDocStates();
        ReLocalizeDynamicStrings();
        AppVersionText = T.F("FeedbackVersion", AppVersion);

        // Shell strings: navigation labels + the language panels' captions and
        // status lines must follow the new language immediately.
        foreach (var item in NavItems)
            item.ApplyLocalization(T);
        foreach (var panel in QuestionPanels)
            panel.ApplyLocalization(T);
        foreach (var panel in AnswerPanels)
            panel.ApplyLocalization(T);

        // Health pills: re-localize the current short states, then re-probe
        // the real provider health ONCE (the async refresh re-localizes the
        // pill texts at the end of every check).
        UpdateTranslationPill();
        ModelSuggestedText = T.F("ModelSuggested", SuggestedOllamaModel, SuggestedWhisperModel);
        _ = RefreshStatusAsync();

        // Force every {Binding T['...']} to re-evaluate.
        OnPropertyChanged(nameof(T));
    }

    private void ApplyLocalizedInitialStrings()
    {
        StatusText = T["StatusReady"];
        AiStatusText = T["AiChecking"];
        UpdateSpeechSetupStatus();
        RecomputeContextStatus();
        UpdateAllDocStates();
        ModelSuggestedText = T.F("ModelSuggested", SuggestedOllamaModel, SuggestedWhisperModel);
        AppVersionText = T.F("FeedbackVersion", AppVersion);
    }

    private void BuildThemeOptions()
    {
        ThemeOptions.Clear();
        ThemeOptions.Add(new PickerOption(ThemeApplier.Light, T["ThemeLight"]));
        ThemeOptions.Add(new PickerOption(ThemeApplier.Dark, T["ThemeDark"]));
    }

    private void BuildLocalizedPickerOptions()
    {
        TranslationEngineOptions.Clear();
        TranslationEngineOptions.Add(new PickerOption("Ollama", T["EngineOllama"]));
        TranslationEngineOptions.Add(new PickerOption("WhisperTranslate", T["EngineWhisper"]));
        TranslationEngineOptions.Add(new PickerOption("ExternalCli", T["EngineExternal"]));

        AnswerLengthOptions.Clear();
        AnswerLengthOptions.Add(new PickerOption("Concise", T["LengthConcise"]));
        AnswerLengthOptions.Add(new PickerOption("Normal", T["LengthNormal"]));
        AnswerLengthOptions.Add(new PickerOption("Detailed", T["LengthDetailed"]));

        AnswerStyleOptions.Clear();
        AnswerStyleOptions.Add(new PickerOption("Concise", T["StyleConcise"]));
        AnswerStyleOptions.Add(new PickerOption("Natural", T["StyleNatural"]));
        AnswerStyleOptions.Add(new PickerOption("Professional", T["StyleProfessional"]));
    }

    private void RecomputeContextStatus()
    {
        var ctx = _services.Pipeline.Context;
        bool hasCv = !string.IsNullOrWhiteSpace(ctx.CvText);
        bool hasJd = !string.IsNullOrWhiteSpace(ctx.JobDescription);
        bool hasNotes = !string.IsNullOrWhiteSpace(ctx.Notes);
        ContextStatus = !hasCv && !hasJd && !hasNotes ? T["ContextNone"]
            : hasCv && hasJd ? T["ContextBothLoaded"]
            : hasCv ? T["ContextCvLoaded"]
            : hasJd ? T["ContextJdLoaded"]
            : T["ContextNotesLoaded"];
    }

    private static string SafeSuggest(Func<string> suggest)
    {
        try { return suggest(); }
        catch { return "unknown"; }
    }

    private void WirePipeline()
    {
        // Unsubscribe from a previous pipeline instance (after a rebuild).
        if (_wiredPipeline is not null)
        {
            _wiredPipeline.TranscriptUpdated -= _hTranscript;
            _wiredPipeline.TranslationUpdated -= _hTranslation;
            _wiredPipeline.AnswerUpdated -= _hAnswer;
            _wiredPipeline.AnswerGenerating -= _hGenerating;
            _wiredPipeline.QuestionSetUpdated -= _hQuestionSet;
            _wiredPipeline.AnswersUpdated -= _hAnswers;
            _wiredPipeline.AudioLevelUpdated -= _hLevel;
            _wiredPipeline.StateChanged -= _hState;
            _wiredPipeline.Error -= _hError;
            _wiredPipeline = null;
        }

        var p = _services.Pipeline;
        _hTranscript = (_, e) => Post(() =>
        {
            TranscriptText = e.Result.Text;
            IsTranscriptProvisional = e.Result.IsProvisional;
        });
        _hTranslation = (_, e) => Post(() =>
        {
            TranslationText = e.Result.HasText ? e.Result.TargetText : string.Empty;
            // Honest, LOCALIZED status (all four UI languages). A genuine
            // translation shows no status line; the raw English StatusMessage
            // on the result is log-only and never displayed.
            _lastTranslationResult = e.Result;
            TranslationStatusText = e.Result.IsTranslated
                ? string.Empty
                : TranslationStatusLocalizer.Localize(T, e.Result);
        });
        _hAnswer = (_, e) => Post(() =>
        {
            // The detected-question panel updates for every emitted suggestion
            // (real or status): the question itself was genuinely detected.
            if (!string.IsNullOrWhiteSpace(e.Suggestion.Question))
                DetectedQuestionText = e.Suggestion.Question;
            // Honest, LOCALIZED answer line (all four UI languages). A real
            // suggestion shows the model text; a failed / unavailable generation
            // shows the localized status line. The raw English message on the
            // suggestion is log-only and never displayed.
            _lastAnswerSuggestion = e.Suggestion;
            AnswerText = e.Suggestion.Status is AnswerStatus.None
                ? e.Suggestion.Answer
                : AnswerStatusLocalizer.Localize(T, e.Suggestion);
            IsAnswerProvisional = e.Suggestion.IsProvisional;
            AnswerIsSuggestion = e.Suggestion.Status is AnswerStatus.None;
            CopyAnswerCommand?.RaiseCanExecuteChanged();
        });
        // "Generating…" state: shown while the model thinks about a question
        // that has no provisional answer on screen yet; replaced by the result.
        _hGenerating = (_, _) => Post(() =>
        {
            _lastAnswerSuggestion = null;
            IsGenerating = true;
            AnswerText = T["AnswerGenerating"];
            IsAnswerProvisional = false;
            AnswerIsSuggestion = false;
            CopyAnswerCommand?.RaiseCanExecuteChanged();
        });
        // Detected-question panel trio: original + one panel per configured
        // language. Every panel keeps its RAW status code so it can be
        // re-localized on a language switch.
        _hQuestionSet = (_, e) => Post(() =>
        {
            _questionSourceLanguage = e.SourceLanguage;
            QuestionPanels.Clear();
            QuestionPanels.Add(CreatePanel("original", "QOriginal", isPrimary: true,
                e.Original, PanelState.Ready, null, null, null, null, e.SourceLanguage));
            foreach (var panel in e.Translations)
            {
                QuestionPanels.Add(CreatePanel(panel.Language, "QTranslation", isPrimary: false,
                    panel.Text, panel.State, panel.Status, panel.StatusMessage, null, null, e.SourceLanguage));
            }
        });

        // Answer panel trio: generated answer (primary language) + local
        // translations, each with an honest pending/unavailable state.
        _hAnswers = (_, e) => Post(() =>
        {
            IsGenerating = false;
            AnswerPanels.Clear();
            foreach (var panel in e.Panels)
            {
                AnswerPanels.Add(CreatePanel(panel.Language, "AnswerLabel", panel.IsPrimary,
                    panel.Text, panel.State, panel.Status, panel.StatusMessage, panel.AnswerStatus, panel.Model,
                    _services.Config.AnswerLanguage));
            }
        });

        // Real capture level -> waveform (bounded history, newest last).
        _hLevel = (_, e) => Post(() =>
        {
            WaveformLevels.Add(e.Level);
            while (WaveformLevels.Count > WaveformCapacity)
                WaveformLevels.RemoveAt(0);
            OnPropertyChanged(nameof(HasWaveform));
        });

        _hState = (_, e) => Post(() => UpdateStatus(e.State));
        // Runtime errors are category-localized (all four UI languages); raw
        // exception text (which may contain local paths) is never shown.
        _hError = (_, e) => Post(() => ShowRuntimeError(e.Error));

        p.TranscriptUpdated += _hTranscript;
        p.TranslationUpdated += _hTranslation;
        p.AnswerUpdated += _hAnswer;
        p.AnswerGenerating += _hGenerating;
        p.QuestionSetUpdated += _hQuestionSet;
        p.AnswersUpdated += _hAnswers;
        p.AudioLevelUpdated += _hLevel;
        p.StateChanged += _hState;
        p.Error += _hError;
        _wiredPipeline = p;
    }

    private void OnCaptureDeviceChanged(object? sender, AudioDeviceEventArgs e)
        => Post(() =>
        {
            RefreshDevices();
            if (IsListening && e.Device is not null)
                StatusText = T["StatusDeviceChanged"];
        });

    private void Post(Action action)
    {
        if (_dispatcher.CheckAccess())
            action();
        else
            _dispatcher.Invoke(action);
    }

    private void RaiseCommandStates()
    {
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        RefreshDevicesCommand.RaiseCanExecuteChanged();
        DownloadModelCommand.RaiseCanExecuteChanged();
        RefreshStatusCommand.RaiseCanExecuteChanged();
        CopyAnswerCommand.RaiseCanExecuteChanged();
        RaiseAnswerCommandStates();
    }

    private void RaiseAnswerCommandStates()
    {
        ShorterCommand?.RaiseCanExecuteChanged();
        LongerCommand?.RaiseCanExecuteChanged();
        RegenerateCommand?.RaiseCanExecuteChanged();
    }

    /// <summary>Builds one language panel with the pipeline's RAW data (re-localizable).</summary>
    private LanguagePanelViewModel CreatePanel(
        string language,
        string captionKey,
        bool isPrimary,
        string? text,
        PanelState state,
        TranslationStatus? translationStatus,
        string? statusMessage,
        AnswerStatus? answerStatus,
        string? model,
        string? sourceLanguage)
    {
        var panel = new LanguagePanelViewModel(language, captionKey, isPrimary) { SourceLanguage = sourceLanguage };
        panel.CopyFailed += (_, _) => ShowKeyedError("ErrorCopyFailed");
        panel.SetContent(text, state, translationStatus, statusMessage, answerStatus, model);
        panel.ApplyLocalization(T);
        return panel;
    }

    // ---- Shorter / Longer / Regenerate (real local re-generation) ----
    private bool _isRegenerating;
    public bool IsRegenerating { get => _isRegenerating; private set { Set(ref _isRegenerating, value); RaiseAnswerCommandStates(); } }

    /// <summary>Regeneration needs an active session (it reuses the session token for Stop).</summary>
    private bool CanRegenerate => IsListening && !IsRegenerating && HasDetectedQuestion;

    private async Task RegenerateAsync(bool? shorter = null, AnswerLength? length = null)
    {
        if (!CanRegenerate)
            return;

        var current = _services.Config.AnswerLength;
        AnswerLength target = shorter switch
        {
            true => current == AnswerLength.Detailed ? AnswerLength.Normal : AnswerLength.Concise,
            false => current == AnswerLength.Concise ? AnswerLength.Normal : AnswerLength.Detailed,
            _ => length ?? current
        };

        IsRegenerating = true;
        try
        {
            // Runs on the pipeline worker path (same cancellation + stale guard);
            // Stop cancels it, so no stale answer can appear afterwards.
            await _services.Pipeline.RegenerateAnswerAsync(target);
        }
        catch (Exception ex)
        {
            ShowRuntimeError(ex);
        }
        finally
        {
            IsRegenerating = false;
        }
    }

    // ---- local log viewer ----
    private async Task RefreshLogsAsync()
    {
        try
        {
            string path = AusbildungsWegInterview.Infrastructure.Logging.AppLogging.DefaultLogFile;
            if (!File.Exists(path))
            {
                LogLines.Clear();
                LogsUnavailable = true;
                return;
            }

            // The writer keeps the file open: read with shared access.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var tail = new Queue<string>(300);
            string? line;
            while ((line = await reader.ReadLineAsync()) is not null)
            {
                tail.Enqueue(line);
                if (tail.Count > 300)
                    tail.Dequeue();
            }

            LogLines.Clear();
            foreach (var l in tail)
                LogLines.Add(l);
            LogsUnavailable = false;
        }
        catch
        {
            LogsUnavailable = true;
        }
        finally
        {
            OnPropertyChanged(nameof(HasLogLines));
        }
    }

    /// <summary>
    /// Copies a NON-SENSITIVE diagnostics summary (version, settings, health,
    /// whether documents are loaded — never their content, never transcripts).
    /// </summary>
    private void CopyDiagnostics()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("MAMADO - AusbildungsWeg Interview");
            sb.AppendLine("version: " + AppVersion);
            sb.AppendLine("ui language: " + _services.Config.UiLanguage + " | theme: " + _services.Config.Theme);
            sb.AppendLine("speech: " + SpeechSetupStatus);
            sb.AppendLine("ai: " + AiStatusText);
            sb.AppendLine("translation engine: " + _services.Config.TranslationProvider + " | " + TranslationPillState);
            sb.AppendLine("capture device: " + (_services.Config.OutputDeviceId ?? "(system default output)"));
            sb.AppendLine("listening: " + IsListening);
            sb.AppendLine("documents in use: cv=" + _cvDoc.IsInUse + ", jd=" + _jdDoc.IsInUse + ", notes=" + _notesDoc.IsInUse);
            Clipboard.SetText(sb.ToString());
        }
        catch
        {
            ShowKeyedError("ErrorCopyFailed");
        }
    }

    /// <summary>Translation pill: mirrors the REAL provider that is configured.</summary>
    private void UpdateTranslationPill()
    {
        var c = _services.Config;
        if (!c.TranslationEnabled || c.TranslationProvider == TranslationProvider.Off)
        {
            TranslationPillReady = false;
            TranslationPillFaulted = false;
            TranslationPillState = T["PillOff"];
            return;
        }

        switch (c.TranslationProvider)
        {
            case TranslationProvider.WhisperTranslate:
                TranslationPillReady = WhisperPillReady;
                TranslationPillFaulted = WhisperPillFaulted;
                TranslationPillState = WhisperPillReady ? T["PillReady"]
                    : WhisperPillFaulted ? T["PillUnavailable"] : T["PillUnknown"];
                break;

            case TranslationProvider.ExternalCli:
                bool cliOk = !string.IsNullOrWhiteSpace(c.TranslatorCliPath) &&
                             System.IO.File.Exists(c.TranslatorCliPath!);
                TranslationPillReady = cliOk;
                TranslationPillFaulted = !cliOk;
                TranslationPillState = cliOk ? T["PillReady"] : T["PillUnavailable"];
                break;

            case TranslationProvider.Ollama:
                TranslationPillReady = OllamaPillReady;
                TranslationPillFaulted = OllamaPillFaulted;
                TranslationPillState = OllamaPillReady ? T["PillReady"]
                    : OllamaPillFaulted ? T["PillUnavailable"] : T["PillUnknown"];
                break;

            default:
                TranslationPillReady = true;
                TranslationPillFaulted = false;
                TranslationPillState = T["PillReady"];
                break;
        }
    }

    /// <summary>Copies the current REAL suggestion to the clipboard (status lines are not copyable).</summary>
    private void CopyAnswer()
    {
        if (!AnswerIsSuggestion)
            return;
        try
        {
            System.Windows.Clipboard.SetText(AnswerText ?? string.Empty);
        }
        catch (Exception)
        {
            // Clipboard can be transiently locked by another app — report, don't crash.
            ShowKeyedError("ErrorCopyFailed");
        }
    }

    private void UpdateStatus(PipelineState state)
    {
        IsListening = state is PipelineState.Listening or PipelineState.Processing;
        StatusText = state switch
        {
            PipelineState.Preparing => T["StatusPreparing"],
            PipelineState.Listening => T["StatusListening"],
            PipelineState.Processing => T["StatusProcessing"],
            PipelineState.Stopping => T["StatusStopping"],
            PipelineState.Faulted => T["StatusFaulted"],
            _ => T["StatusStopped"]
        };
        StatusBrush = state switch
        {
            PipelineState.Listening or PipelineState.Processing => StatusListeningBrush,
            PipelineState.Preparing => StatusPreparingBrush,
            PipelineState.Faulted => StatusFaultBrush,
            _ => StatusIdleBrush
        };
        RaiseCommandStates();
    }

    private async Task StartAsync()
    {
        IsBusy = true;
        ClearErrorState();
        // Fresh session: previous transcript / translation / answer content is
        // cleared so the live area only shows what the new session hears.
        TranscriptText = string.Empty;
        IsTranscriptProvisional = false;
        TranslationText = string.Empty;
        TranslationStatusText = string.Empty;
        _lastTranslationResult = null;
        AnswerText = string.Empty;
        IsAnswerProvisional = false;
        AnswerIsSuggestion = false;
        CopyAnswerCommand?.RaiseCanExecuteChanged();
        DetectedQuestionText = string.Empty;
        // Fresh session: the panel trios and the waveform start empty (no stale
        // content from the previous session is ever shown as if it were live).
        QuestionPanels.Clear();
        AnswerPanels.Clear();
        WaveformLevels.Clear();
        OnPropertyChanged(nameof(HasWaveform));
        try
        {
            SaveConfig();
            await _services.Pipeline.StartAsync();
        }
        catch (Exception ex)
        {
            IsListening = false; // never claim "listening" after a failed start
            ShowRuntimeError(ex);
            StatusText = T["StatusFaulted"];
            StatusBrush = StatusFaultBrush;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StopAsync()
    {
        IsBusy = true;
        try
        {
            await _services.Pipeline.StopAsync();
        }
        catch (Exception ex)
        {
            ShowRuntimeError(ex);
        }
        finally
        {
            IsListening = false;
            IsBusy = false;
        }
    }

    /// <summary>
    /// Applies whisper/translation settings that require a fresh recognizer or
    /// translator (executable, model, translation toggle). Blocked while listening.
    /// </summary>
    private void ApplySpeechSettings()
    {
        if (_services.Pipeline.IsRunning)
        {
            ShowKeyedError("ErrorStopBeforeSettings");
            return;
        }
        try
        {
            SaveConfig();
            _services.RebuildPipeline();
            WirePipeline();
            UpdateSpeechSetupStatus();
        }
        catch (Exception ex)
        {
            // Category-localized: "Stop listening before…", "Unknown model …",
            // missing executable/model, or the generic runtime message.
            ShowRuntimeError(ex);
        }
    }

    private void UpdateSpeechSetupStatus()
    {
        try
        {
            var c = _services.Config;
            // Adapter shown by name only (no local paths in the UI).
            string engine = !string.IsNullOrWhiteSpace(c.WhisperExecutablePath)
                ? (System.IO.File.Exists(c.WhisperExecutablePath!)
                    ? T["SpeechEngineCli"]
                    : T["SpeechEngineCliMissing"])
                : T["SpeechEngineNative"];

            string modelPath = !string.IsNullOrWhiteSpace(c.WhisperModelPath)
                ? c.WhisperModelPath!
                : _services.Models.ResolvePath(c.WhisperModel);
            bool modelOk = System.IO.File.Exists(modelPath);

            SpeechSetupStatus = T.F("SpeechSetupLine", engine, c.WhisperModel, modelOk ? T["SpeechModelReady"] : T["SpeechModelMissing"]);

            // Pill: "ready" ONLY when the engine resolves AND the model file exists.
            bool engineOk = string.IsNullOrWhiteSpace(c.WhisperExecutablePath) ||
                            System.IO.File.Exists(c.WhisperExecutablePath!);
            WhisperPillReady = engineOk && modelOk;
            WhisperPillFaulted = !WhisperPillReady;
            WhisperPillState = WhisperPillReady
                ? T["PillReady"]
                : engineOk ? T["PillModelMissing"] : T["PillUnavailable"];
        }
        catch
        {
            SpeechSetupStatus = T["SpeechStatusUnknown"];
            WhisperPillReady = false;
            WhisperPillFaulted = true;
            WhisperPillState = T["PillUnknown"];
        }
        UpdateTranslationPill();
    }

    private void SaveConfig()
    {
        try { _services.ConfigStore.Save(_services.Config); }
        catch { /* config persistence must never crash the UI */ }
    }

    private void RefreshDevices()
    {
        var previous = SelectedOutputDevice?.Id;
        OutputDevices.Clear();
        try
        {
            foreach (var d in _services.Audio.GetOutputDevices())
                OutputDevices.Add(d);
        }
        catch (Exception)
        {
            // No raw ex.Message in the UI (it can contain local paths) — the
            // category alone is actionable; details belong in the log, not the panel.
            ShowKeyedError("ErrorCouldNotEnumerateDevices");
            return;
        }

        SelectedOutputDevice =
            OutputDevices.FirstOrDefault(d => d.Id == previous)
            ?? OutputDevices.FirstOrDefault(d => d.IsDefault)
            ?? OutputDevices.FirstOrDefault();

        if (OutputDevices.Count == 0)
            ShowKeyedError("ErrorNoOutputDevice");
    }

    private async Task RefreshStatusAsync()
    {
        IsBusy = true;
        try
        {
            bool available = await _services.Ollama.IsAvailableAsync();
            if (!available)
            {
                AiStatusText = T["AiUnavailable"];
                OllamaPillReady = false;
                OllamaPillFaulted = true;
                OllamaPillState = T["PillUnavailable"];
            }
            else
            {
                bool modelOk = await _services.Ollama.ModelAvailableAsync(_services.Config.OllamaModel);
                AiStatusText = modelOk
                    ? T.F("AiModelReady", _services.Config.OllamaModel)
                    : T.F("AiModelMissing", _services.Config.OllamaModel);
                OllamaPillReady = modelOk;
                OllamaPillFaulted = !modelOk;
                OllamaPillState = modelOk ? T["PillReady"] : T["PillModelMissing"];
            }
        }
        catch
        {
            AiStatusText = T["AiStatusUnknown"];
            OllamaPillReady = false;
            OllamaPillFaulted = false;
            OllamaPillState = T["PillUnknown"];
        }
        finally
        {
            IsBusy = false;
        }
        // The translation pill mirrors the real provider health (Ollama / whisper / CLI).
        UpdateTranslationPill();
        UpdateWhisperModelStatus();
    }

    private void UpdateWhisperModelStatus()
    {
        UpdateSpeechSetupStatus();
    }

    private async Task DownloadModelAsync()
    {
        ClearErrorState();
        IsBusy = true;
        SpeechSetupStatus = T.F("DownloadingModel", _services.Config.WhisperModel);
        try
        {
            await _services.Models.DownloadAsync(_services.Config.WhisperModel);
            UpdateSpeechSetupStatus();
        }
        catch (Exception)
        {
            // No raw ex.Message in the UI (it can contain local paths).
            ShowKeyedError("ErrorModelDownload");
            UpdateSpeechSetupStatus();
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- CV / job description: import (PDF/DOCX/TXT, content-sniffed) -> review box -> Use ----

    private async Task ImportDocumentAsync(ReviewableDocument doc, string slot)
    {
        var dlg = new OpenFileDialog
        {
            Title = slot switch
            {
                "cv" => T["LoadCvTitle"],
                "jd" => T["LoadJdTitle"],
                _ => T["LoadNotesTitle"]
            },
            Filter = DocFilter
        };
        if (dlg.ShowDialog() != true)
            return;

        IsBusy = true;
        try
        {
            // Extraction runs on the thread pool (PdfPig / ZIP / text read);
            // the UI thread only awaits, so long files never freeze the window.
            var result = await Task.Run(() => _docImporter.ImportAsync(dlg.FileName));
            if (result.Success)
            {
                ClearErrorState();
                doc.SetImported(result.Text!); // goes to REVIEW; old grounding text stays active
                ChipFor(slot)?.SetImported(
                    System.IO.Path.GetFileName(dlg.FileName), result.PageCount, result.Text!.Length);
                ApplyDocumentToContextIfInUse(doc, slot);
                UpdateDocState(doc, slot);
                if (result.Truncated)
                    ShowKeyedError("ErrorDocTruncated", _docImporter.MaxExtractedChars);
            }
            else
            {
                // Categorized, localized error; the file path is NOT shown (privacy).
                ShowDocError(result.Error);
            }
        }
        catch (Exception)
        {
            ShowDocError(DocumentImportError.Corrupt);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// A replacement import keeps the OLD grounding text active until the user
    /// accepts the new one — re-applying here would silently swap the context.
    /// </summary>
    private void ApplyDocumentToContextIfInUse(ReviewableDocument doc, string slot)
    {
        if (doc.State == DocumentState.InUse)
            ApplyDocumentToContext(doc, slot);
        else
            RecomputeContextStatus();
    }

    private DocumentChipViewModel? ChipFor(string slot) => slot switch
    {
        "cv" => CvChip,
        "jd" => JdChip,
        "notes" => NotesChip,
        _ => null
    };

    private void UseDocument(ReviewableDocument doc, string slot)
    {
        if (doc.TryApply(out string errorKey))
        {
            ClearErrorState();
            ApplyDocumentToContext(doc, slot);
        }
        else
        {
            ShowKeyedError(errorKey);
        }
        UpdateDocState(doc, slot);
    }

    private void ClearDocument(ReviewableDocument doc, string slot)
    {
        doc.Clear(); // removes box text AND stops grounding with it
        ChipFor(slot)?.Clear();
        ClearErrorState();
        ApplyDocumentToContext(doc, slot);
        UpdateDocState(doc, slot);
        RaiseDocCommandStates();
    }

    private void ApplyConfigToUi()
    {
        var c = _services.Config;
        _selectedSourceLanguage = c.SourceLanguage ?? "auto";
        _selectedTargetLanguage = c.TranslationLanguage;
        _selectedAnswerLanguage = c.AnswerLanguage;
        _selectedAnswerLength = AnswerLengthOptions.FirstOrDefault(o => o.Value == c.AnswerLength.ToString());
        _selectedAnswerStyle = AnswerStyleOptions.FirstOrDefault(o => o.Value == c.AnswerStyle.ToString());
        _selectedCefrLevel = c.AnswerLevel.ToString();
        _ollamaModel = c.OllamaModel;
        _selectedWhisperModel = c.WhisperModel;
        _whisperExecutablePath = c.WhisperExecutablePath ?? string.Empty;
        _whisperModelPath = c.WhisperModelPath ?? string.Empty;
        _selectedTranslationEngine = TranslationEngineOptions.FirstOrDefault(o => o.Value == c.TranslationProvider.ToString());
        _translatorCliPath = c.TranslatorCliPath ?? string.Empty;
        _translatorCliArgs = c.TranslatorCliArgsTemplate ?? string.Empty;
        _translationEnabled = c.TranslationEnabled;
        _alwaysOnTop = c.AlwaysOnTop;
    }
}
