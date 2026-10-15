using System.Collections.ObjectModel;
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
        private set { Set(ref _detectedQuestionText, value); OnPropertyChanged(nameof(HasDetectedQuestion)); }
    }
    public bool HasDetectedQuestion => !string.IsNullOrEmpty(DetectedQuestionText);

    private string _answerText = string.Empty;
    public string AnswerText { get => _answerText; private set => Set(ref _answerText, value); }

    private bool _isAnswerProvisional;
    public bool IsAnswerProvisional { get => _isAnswerProvisional; private set => Set(ref _isAnswerProvisional, value); }

    private string _errorText = string.Empty;
    public string ErrorText
    {
        get => _errorText;
        private set { Set(ref _errorText, value); OnPropertyChanged(nameof(HasError)); }
    }
    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    private string _aiStatusText = string.Empty;
    public string AiStatusText { get => _aiStatusText; private set => Set(ref _aiStatusText, value); }

    private string _speechSetupStatus = string.Empty;
    public string SpeechSetupStatus { get => _speechSetupStatus; private set => Set(ref _speechSetupStatus, value); }

    private string _contextStatus = string.Empty;
    public string ContextStatus { get => _contextStatus; private set => Set(ref _contextStatus, value); }

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

    // ---- commands ----
    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand RefreshDevicesCommand { get; }
    public RelayCommand DownloadModelCommand { get; }
    public RelayCommand LoadCvCommand { get; }
    public RelayCommand LoadJobCommand { get; }
    public RelayCommand ClearErrorCommand { get; }
    public RelayCommand RefreshStatusCommand { get; }
    public RelayCommand ToggleThemeCommand { get; }

    // ---- pipeline event wiring (unsubscribed on rebuild) ----
    private EventHandler<TranscriptUpdatedEventArgs>? _hTranscript;
    private EventHandler<TranslationUpdatedEventArgs>? _hTranslation;
    private EventHandler<AnswerUpdatedEventArgs>? _hAnswer;
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
        LoadCvCommand = new RelayCommand(_ => _ = LoadCvAsync());
        LoadJobCommand = new RelayCommand(_ => _ = LoadJobAsync());
        ClearErrorCommand = new RelayCommand(_ => ErrorText = string.Empty);
        RefreshStatusCommand = new RelayCommand(_ => _ = RefreshStatusAsync(), _ => !IsBusy);
        ToggleThemeCommand = new RelayCommand(_ => ToggleTheme());

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
        ModelSuggestedText = T.F("ModelSuggested", SuggestedOllamaModel, SuggestedWhisperModel);
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
        ContextStatus = !hasCv && !hasJd ? T["ContextNone"]
            : hasCv && hasJd ? T["ContextBothLoaded"]
            : hasCv ? T["ContextCvLoaded"]
            : T["ContextJdLoaded"];
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
            TranslationStatusText = e.Result.StatusMessage ?? string.Empty;
        });
        _hAnswer = (_, e) => Post(() =>
        {
            if (e.Suggestion.HasAnswer)
                DetectedQuestionText = e.Suggestion.Question;
            AnswerText = e.Suggestion.Answer;
            IsAnswerProvisional = e.Suggestion.IsProvisional;
        });
        _hState = (_, e) => Post(() => UpdateStatus(e.State));
        // Runtime errors are category-localized (all four UI languages); raw
        // exception text (which may contain local paths) is never shown.
        _hError = (_, e) => Post(() => ErrorText = SpeechErrorLocalizer.Localize(T, e.Error));

        p.TranscriptUpdated += _hTranscript;
        p.TranslationUpdated += _hTranslation;
        p.AnswerUpdated += _hAnswer;
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
        ErrorText = string.Empty;
        // Fresh session: previous transcript / translation / answer content is
        // cleared so the live area only shows what the new session hears.
        TranscriptText = string.Empty;
        IsTranscriptProvisional = false;
        TranslationText = string.Empty;
        TranslationStatusText = string.Empty;
        AnswerText = string.Empty;
        IsAnswerProvisional = false;
        DetectedQuestionText = string.Empty;
        try
        {
            SaveConfig();
            await _services.Pipeline.StartAsync();
        }
        catch (Exception ex)
        {
            IsListening = false; // never claim "listening" after a failed start
            ErrorText = SpeechErrorLocalizer.Localize(T, ex);
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
            ErrorText = SpeechErrorLocalizer.Localize(T, ex);
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
            ErrorText = T["ErrorStopBeforeSettings"];
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
            ErrorText = SpeechErrorLocalizer.Localize(T, ex);
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
        }
        catch
        {
            SpeechSetupStatus = T["SpeechStatusUnknown"];
        }
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
        catch (Exception ex)
        {
            ErrorText = T.F("ErrorCouldNotEnumerateDevices", ex.Message);
            return;
        }

        SelectedOutputDevice =
            OutputDevices.FirstOrDefault(d => d.Id == previous)
            ?? OutputDevices.FirstOrDefault(d => d.IsDefault)
            ?? OutputDevices.FirstOrDefault();

        if (OutputDevices.Count == 0)
            ErrorText = T["ErrorNoOutputDevice"];
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
            }
            else
            {
                bool modelOk = await _services.Ollama.ModelAvailableAsync(_services.Config.OllamaModel);
                AiStatusText = modelOk
                    ? T.F("AiModelReady", _services.Config.OllamaModel)
                    : T.F("AiModelMissing", _services.Config.OllamaModel);
            }
        }
        catch
        {
            AiStatusText = T["AiStatusUnknown"];
        }
        finally
        {
            IsBusy = false;
        }
        UpdateWhisperModelStatus();
    }

    private void UpdateWhisperModelStatus()
    {
        UpdateSpeechSetupStatus();
    }

    private async Task DownloadModelAsync()
    {
        ErrorText = string.Empty;
        IsBusy = true;
        SpeechSetupStatus = T.F("DownloadingModel", _services.Config.WhisperModel);
        try
        {
            await _services.Models.DownloadAsync(_services.Config.WhisperModel);
            UpdateSpeechSetupStatus();
        }
        catch (Exception ex)
        {
            ErrorText = T.F("ErrorModelDownload", ex.Message);
            UpdateSpeechSetupStatus();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadCvAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = T["LoadCvTitle"],
            Filter = T.F("FileFilter", T["FileFilterText"], T["FileFilterAll"])
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                var text = await System.IO.File.ReadAllTextAsync(dlg.FileName);
                _services.Pipeline.Context = _services.Pipeline.Context.WithCv(text);
                RecomputeContextStatus();
            }
            catch (Exception ex)
            {
                ErrorText = T.F("ErrorCouldNotReadCv", ex.Message);
            }
        }
    }

    private async Task LoadJobAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = T["LoadJdTitle"],
            Filter = T.F("FileFilter", T["FileFilterText"], T["FileFilterAll"])
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                var text = await System.IO.File.ReadAllTextAsync(dlg.FileName);
                _services.Pipeline.Context = _services.Pipeline.Context.WithJobDescription(text);
                RecomputeContextStatus();
            }
            catch (Exception ex)
            {
                ErrorText = T.F("ErrorCouldNotReadJd", ex.Message);
            }
        }
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
