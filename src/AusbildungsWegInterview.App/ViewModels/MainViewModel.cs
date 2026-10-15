using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using AusbildungsWegInterview.Core.Audio;
using AusbildungsWegInterview.Core.Common;
using AusbildungsWegInterview.Core.Pipeline;

namespace AusbildungsWegInterview.App.ViewModels;

/// <summary>
/// Main floating-window view model. Binds to the InterviewPipeline and exposes
/// UI state + commands. All provider access is through abstractions (the VM never
/// talks to a specific AI runtime directly).
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
    public ObservableCollection<string> WhisperModelOptions { get; } = new() { "tiny", "base", "small", "medium" };

    // ---- settings-backed (persisted to config) ----
    private AudioDevice? _selectedOutputDevice;
    public AudioDevice? SelectedOutputDevice
    {
        get => _selectedOutputDevice;
        set { if (Set(ref _selectedOutputDevice, value)) _services.Config.OutputDeviceId = value?.Id; }
    }

    private string _selectedSourceLanguage = "auto";
    public string SelectedSourceLanguage
    {
        get => _selectedSourceLanguage;
        set { if (Set(ref _selectedSourceLanguage, value)) _services.Config.SourceLanguage = value == "auto" ? null : value; }
    }

    private string _selectedTargetLanguage = "en";
    public string SelectedTargetLanguage
    {
        get => _selectedTargetLanguage;
        set { if (Set(ref _selectedTargetLanguage, value)) _services.Config.TranslationLanguage = value; }
    }

    private string _selectedAnswerLanguage = "de";
    public string SelectedAnswerLanguage
    {
        get => _selectedAnswerLanguage;
        set { if (Set(ref _selectedAnswerLanguage, value)) _services.Config.AnswerLanguage = value; }
    }

    private string _selectedAnswerLength = "Normal";
    public string SelectedAnswerLength
    {
        get => _selectedAnswerLength;
        set { if (Set(ref _selectedAnswerLength, value)) _services.Config.AnswerLength = value switch { "Concise" => AnswerLength.Concise, "Detailed" => AnswerLength.Detailed, _ => AnswerLength.Normal }; }
    }

    private string _selectedWhisperModel = "base";
    public string SelectedWhisperModel
    {
        get => _selectedWhisperModel;
        set { if (Set(ref _selectedWhisperModel, value)) _services.Config.WhisperModel = value; }
    }

    private bool _alwaysOnTop;
    public bool AlwaysOnTop
    {
        get => _alwaysOnTop;
        set { if (Set(ref _alwaysOnTop, value)) _services.Config.AlwaysOnTop = value; }
    }

    // ---- display state (private set + auto-notify) ----
    private string _statusText = "Ready";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    private bool _isListening;
    public bool IsListening { get => _isListening; private set => Set(ref _isListening, value); }

    private string _transcriptText = string.Empty;
    public string TranscriptText { get => _transcriptText; private set => Set(ref _transcriptText, value); }

    private bool _isTranscriptProvisional;
    public bool IsTranscriptProvisional { get => _isTranscriptProvisional; private set => Set(ref _isTranscriptProvisional, value); }

    private string _translationText = string.Empty;
    public string TranslationText { get => _translationText; private set => Set(ref _translationText, value); }

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

    private string _whisperModelStatus = "Speech model: checking…";
    public string WhisperModelStatus { get => _whisperModelStatus; private set => Set(ref _whisperModelStatus, value); }

    private string _contextStatus = "No CV / job description loaded.";
    public string ContextStatus { get => _contextStatus; private set => Set(ref _contextStatus, value); }

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

    public MainViewModel(AppServices services, Dispatcher dispatcher)
    {
        _services = services;
        _dispatcher = dispatcher;

        StartCommand = new RelayCommand(_ => _ = StartAsync(), _ => !IsListening);
        StopCommand = new RelayCommand(_ => _ = StopAsync(), _ => IsListening);
        RefreshDevicesCommand = new RelayCommand(_ => RefreshDevices());
        DownloadModelCommand = new RelayCommand(_ => _ = DownloadModelAsync());
        LoadCvCommand = new RelayCommand(_ => _ = LoadCvAsync());
        LoadJobCommand = new RelayCommand(_ => _ = LoadJobAsync());
        ClearErrorCommand = new RelayCommand(_ => ErrorText = string.Empty);

        WirePipeline();
        RefreshDevices();
        ApplyConfigToUi();
        _ = RefreshStatusAsync();
    }

    private void WirePipeline()
    {
        var p = _services.Pipeline;
        p.TranscriptUpdated += (_, e) => Post(() =>
        {
            TranscriptText = e.Result.Text;
            IsTranscriptProvisional = e.Result.IsProvisional;
        });
        p.TranslationUpdated += (_, e) => Post(() => TranslationText = e.Result.TargetText);
        p.AnswerUpdated += (_, e) => Post(() =>
        {
            AnswerText = e.Suggestion.Answer;
            IsAnswerProvisional = e.Suggestion.IsProvisional;
        });
        p.StateChanged += (_, e) => Post(() => UpdateStatus(e.State));
        p.Error += (_, e) => Post(() => ErrorText = e.Error.Message);
    }

    private void Post(Action action)
    {
        if (_dispatcher.CheckAccess())
            action();
        else
            _dispatcher.Invoke(action);
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
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
    }

    private async Task StartAsync()
    {
        ErrorText = string.Empty;
        try
        {
            _services.ConfigStore.Save(_services.Config);
            await _services.Pipeline.StartAsync();
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            StatusText = "Error - see below";
        }
    }

    private async Task StopAsync()
    {
        try
        {
            await _services.Pipeline.StopAsync();
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
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
        try
        {
            bool available = await _services.Ollama.IsAvailableAsync();
            AiStatusText = available
                ? "Local AI (Ollama): running"
                : "Local AI (Ollama): not running - answer suggestions disabled (transcription & translation still work)";
        }
        catch
        {
            AiStatusText = "Local AI (Ollama): status unknown";
        }
        UpdateWhisperModelStatus();
    }

    private void UpdateWhisperModelStatus()
    {
        try
        {
            bool present = _services.Models.IsPresent(_services.Config.WhisperModel);
            WhisperModelStatus = present
                ? $"Speech model '{_services.Config.WhisperModel}': ready"
                : $"Speech model '{_services.Config.WhisperModel}': download required (press Download)";
        }
        catch
        {
            WhisperModelStatus = "Speech model: status unknown";
        }
    }

    private async Task DownloadModelAsync()
    {
        ErrorText = string.Empty;
        WhisperModelStatus = $"Downloading '{_services.Config.WhisperModel}'…";
        try
        {
            await _services.Models.DownloadAsync(_services.Config.WhisperModel);
            UpdateWhisperModelStatus();
        }
        catch (Exception ex)
        {
            ErrorText = "Model download failed: " + ex.Message;
            UpdateWhisperModelStatus();
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
        _selectedWhisperModel = c.WhisperModel;
        _alwaysOnTop = c.AlwaysOnTop;
    }
}
