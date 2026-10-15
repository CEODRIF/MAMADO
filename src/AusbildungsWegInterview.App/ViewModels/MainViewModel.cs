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

namespace AusbildungsWegInterview.App.ViewModels;

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
    public ObservableCollection<string> AnswerLengthOptions { get; } = new() { "Concise", "Normal", "Detailed" };
    public ObservableCollection<string> AnswerStyleOptions { get; } = new() { "Concise", "Natural", "Professional" };
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

    public ObservableCollection<string> TranslationEngineOptions { get; } = new()
        { "Ollama (local LLM)", "whisper.cpp (→ English only)", "External CLI (opus-mt)" };

    private string _selectedTranslationEngine = "Ollama (local LLM)";
    public string SelectedTranslationEngine
    {
        get => _selectedTranslationEngine;
        set
        {
            if (!Set(ref _selectedTranslationEngine, value)) return;
            _services.Config.TranslationProvider = value switch
            {
                "whisper.cpp (→ English only)" => TranslationProvider.WhisperTranslate,
                "External CLI (opus-mt)" => TranslationProvider.ExternalCli,
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

    private string _selectedAnswerLength = "Normal";
    public string SelectedAnswerLength
    {
        get => _selectedAnswerLength;
        set { if (Set(ref _selectedAnswerLength, value)) { _services.Config.AnswerLength = value switch { "Concise" => AnswerLength.Concise, "Detailed" => AnswerLength.Detailed, _ => AnswerLength.Normal }; SaveConfig(); } }
    }

    private string _selectedAnswerStyle = "Natural";
    public string SelectedAnswerStyle
    {
        get => _selectedAnswerStyle;
        set { if (Set(ref _selectedAnswerStyle, value)) { _services.Config.AnswerStyle = value switch { "Concise" => AnswerStyle.Concise, "Professional" => AnswerStyle.Professional, _ => AnswerStyle.Natural }; SaveConfig(); } }
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
    private string _statusText = "Ready";
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

    private string _aiStatusText = "Local AI (Ollama): checking…";
    public string AiStatusText { get => _aiStatusText; private set => Set(ref _aiStatusText, value); }

    private string _speechSetupStatus = "Speech engine: checking…";
    public string SpeechSetupStatus { get => _speechSetupStatus; private set => Set(ref _speechSetupStatus, value); }

    private string _contextStatus = "No CV / job description loaded.";
    public string ContextStatus { get => _contextStatus; private set => Set(ref _contextStatus, value); }

    // Conservative, RAM-based model suggestions (informational; nothing is downloaded automatically).
    public string SuggestedOllamaModel { get; }
    public string SuggestedWhisperModel { get; }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set { Set(ref _isBusy, value); RaiseCommandStates(); } }

    private Brush _statusBrush = Brushes.Gray;
    public Brush StatusBrush { get => _statusBrush; private set => Set(ref _statusBrush, value); }

    // ---- commands ----
    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand RefreshDevicesCommand { get; }
    public RelayCommand DownloadModelCommand { get; }
    public RelayCommand LoadCvCommand { get; }
    public RelayCommand LoadJobCommand { get; }
    public RelayCommand ClearErrorCommand { get; }
    public RelayCommand RefreshStatusCommand { get; }

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

        StartCommand = new RelayCommand(_ => _ = StartAsync(), _ => !IsListening && !IsBusy);
        StopCommand = new RelayCommand(_ => _ = StopAsync(), _ => IsListening && !IsBusy);
        RefreshDevicesCommand = new RelayCommand(_ => RefreshDevices(), _ => !IsBusy);
        DownloadModelCommand = new RelayCommand(_ => _ = DownloadModelAsync(), _ => !IsBusy);
        LoadCvCommand = new RelayCommand(_ => _ = LoadCvAsync());
        LoadJobCommand = new RelayCommand(_ => _ = LoadJobAsync());
        ClearErrorCommand = new RelayCommand(_ => ErrorText = string.Empty);
        RefreshStatusCommand = new RelayCommand(_ => _ = RefreshStatusAsync(), _ => !IsBusy);

        SuggestedOllamaModel = SafeSuggest(AusbildungsWegInterview.Infrastructure.SystemInfo.RecommendOllamaModel);
        SuggestedWhisperModel = SafeSuggest(AusbildungsWegInterview.Infrastructure.SystemInfo.RecommendWhisperModel);

        // OS-level device changes (default moved / device lost) are handled by the
        // capture; here we keep the device list + selection in sync and notify the user.
        _services.Audio.DeviceChanged += OnCaptureDeviceChanged;

        WirePipeline();
        RefreshDevices();
        ApplyConfigToUi();
        UpdateSpeechSetupStatus();
        _ = RefreshStatusAsync();
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
        _hError = (_, e) => Post(() => ErrorText = e.Error.Message);

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
                StatusText = "Output device changed - capture re-opened on the new device";
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
            PipelineState.Preparing => "Preparing models…",
            PipelineState.Listening => "Listening",
            PipelineState.Processing => "Processing speech…",
            PipelineState.Stopping => "Stopping…",
            PipelineState.Faulted => "Error - see below",
            _ => "Stopped"
        };
        StatusBrush = state switch
        {
            PipelineState.Listening or PipelineState.Processing => Brushes.LimeGreen,
            PipelineState.Preparing => Brushes.Gold,
            PipelineState.Faulted => Brushes.OrangeRed,
            _ => Brushes.Gray
        };
        RaiseCommandStates();
    }

    private async Task StartAsync()
    {
        IsBusy = true;
        ErrorText = string.Empty;
        try
        {
            SaveConfig();
            await _services.Pipeline.StartAsync();
        }
        catch (Exception ex)
        {
            IsListening = false; // never claim "listening" after a failed start
            ErrorText = ex.Message;
            StatusText = "Error - see below";
            StatusBrush = Brushes.OrangeRed;
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
            ErrorText = ex.Message;
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
            ErrorText = "Stop listening before changing the speech engine or translation settings.";
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
            ErrorText = "Could not apply the speech settings: " + ex.Message;
        }
    }

    private void UpdateSpeechSetupStatus()
    {
        try
        {
            var c = _services.Config;
            string engine = !string.IsNullOrWhiteSpace(c.WhisperExecutablePath)
                ? (System.IO.File.Exists(c.WhisperExecutablePath!)
                    ? "whisper.cpp CLI"
                    : "whisper.cpp CLI (executable not found)")
                : "whisper.cpp native library";

            string modelPath = !string.IsNullOrWhiteSpace(c.WhisperModelPath)
                ? c.WhisperModelPath!
                : _services.Models.ResolvePath(c.WhisperModel);
            bool modelOk = System.IO.File.Exists(modelPath);

            SpeechSetupStatus = engine + " | model '" + c.WhisperModel + "': " +
                (modelOk ? "ready" : "MISSING - download it or set the model path");
        }
        catch
        {
            SpeechSetupStatus = "Speech engine: status unknown";
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
            ErrorText = "Could not enumerate audio devices: " + ex.Message;
            return;
        }

        SelectedOutputDevice =
            OutputDevices.FirstOrDefault(d => d.Id == previous)
            ?? OutputDevices.FirstOrDefault(d => d.IsDefault)
            ?? OutputDevices.FirstOrDefault();

        if (OutputDevices.Count == 0)
            ErrorText = "No active audio output device found. Connect speakers/headphones and press Refresh.";
    }

    private async Task RefreshStatusAsync()
    {
        IsBusy = true;
        try
        {
            bool available = await _services.Ollama.IsAvailableAsync();
            if (!available)
            {
                AiStatusText = "Local AI (Ollama): not running - answer suggestions disabled (transcription still works)";
            }
            else
            {
                bool modelOk = await _services.Ollama.ModelAvailableAsync(_services.Config.OllamaModel);
                AiStatusText = modelOk
                    ? "Local AI (Ollama): running, model '" + _services.Config.OllamaModel + "' ready"
                    : "Local AI (Ollama): running, model '" + _services.Config.OllamaModel + "' MISSING - run: ollama pull " + _services.Config.OllamaModel;
            }
        }
        catch
        {
            AiStatusText = "Local AI (Ollama): status unknown";
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
        SpeechSetupStatus = "Downloading model '" + _services.Config.WhisperModel + "'…";
        try
        {
            await _services.Models.DownloadAsync(_services.Config.WhisperModel);
            UpdateSpeechSetupStatus();
        }
        catch (Exception ex)
        {
            ErrorText = "Model download failed: " + ex.Message;
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
            Title = "Select CV (text file)",
            Filter = "Text files (*.txt;*.md)|*.txt;*.md|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                var text = await System.IO.File.ReadAllTextAsync(dlg.FileName);
                _services.Pipeline.Context = _services.Pipeline.Context.WithCv(text);
                ContextStatus = "CV loaded (kept local only).";
            }
            catch (Exception ex)
            {
                ErrorText = "Could not read CV: " + ex.Message;
            }
        }
    }

    private async Task LoadJobAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select job description (text file)",
            Filter = "Text files (*.txt;*.md)|*.txt;*.md|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                var text = await System.IO.File.ReadAllTextAsync(dlg.FileName);
                _services.Pipeline.Context = _services.Pipeline.Context.WithJobDescription(text);
                ContextStatus = "Job description loaded (kept local only).";
            }
            catch (Exception ex)
            {
                ErrorText = "Could not read job description: " + ex.Message;
            }
        }
    }

    private void ApplyConfigToUi()
    {
        var c = _services.Config;
        _selectedSourceLanguage = c.SourceLanguage ?? "auto";
        _selectedTargetLanguage = c.TranslationLanguage;
        _selectedAnswerLanguage = c.AnswerLanguage;
        _selectedAnswerLength = c.AnswerLength.ToString();
        _selectedAnswerStyle = c.AnswerStyle.ToString();
        _selectedWhisperModel = c.WhisperModel;
        _whisperExecutablePath = c.WhisperExecutablePath ?? string.Empty;
        _whisperModelPath = c.WhisperModelPath ?? string.Empty;
        _selectedTranslationEngine = c.TranslationProvider switch
        {
            TranslationProvider.WhisperTranslate => "whisper.cpp (→ English only)",
            TranslationProvider.ExternalCli => "External CLI (opus-mt)",
            _ => "Ollama (local LLM)"
        };
        _translatorCliPath = c.TranslatorCliPath ?? string.Empty;
        _translatorCliArgs = c.TranslatorCliArgsTemplate ?? string.Empty;
        _translationEnabled = c.TranslationEnabled;
        _alwaysOnTop = c.AlwaysOnTop;
    }
}
