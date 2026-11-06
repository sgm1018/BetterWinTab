using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Threading.Channels;
using BetterWinTab.Models;
using BetterWinTab.Models.Calls;
using BetterWinTab.Models.Transcription;
using BetterWinTab.Services;
using BetterWinTab.Services.Calls;
using BetterWinTab.Services.Calls.Ai;
using BetterWinTab.Services.Transcription;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using Windows.Storage.Pickers;
using WinRT.Interop;
using NAudio.Wave;

namespace BetterWinTab.ViewModels;

/// <summary>Recording library, capture lifecycle and report generation for the Calls workspace.</summary>
public partial class CallsViewModel : ObservableObject
{
    private readonly AudioCaptureService _capture;
    private readonly AudioDeviceService _devices;
    private readonly AudioRecordingStoreService _recordings;
    private readonly ReportGenerationService _reports;
    private readonly GeneratedDocumentStore _documents;
    private readonly CredentialStore _credentials;
    private readonly AppSettings _appSettings;
    private readonly SettingsService _settingsService;
    private readonly AsrModelStore _asrModelStore = new();
    private readonly DispatcherQueue? _dispatcher = DispatcherQueue.GetForCurrentThread();
    private CancellationTokenSource? _captureLifetime;
    private string? _activeRecordingId;

    public ObservableCollection<AudioDeviceInfo> Microphones { get; } = new();
    public ObservableCollection<AudioDeviceInfo> Outputs { get; } = new();
    public ObservableCollection<AudioRecordingMetadata> Recordings { get; } = new();
    public ObservableCollection<GeneratedDocument> Reports { get; } = new();
    public ObservableCollection<string> Languages { get; } = new();
    public ObservableCollection<string> VisibleLanguages { get; } = new();
    public ObservableCollection<ChatModel> ChatModels { get; } = new();
    public IReadOnlyList<BetterWinTab.Models.Transcription.AsrModel> AsrModels { get; } =
        BetterWinTab.Models.Transcription.AsrModelCatalog.Models.Where(model => model.ExecutionMode == AsrExecutionMode.TrueStreaming).ToArray();
    public IReadOnlyList<BetterWinTab.Models.Transcription.DiarizationModelAsset> DiarizationModels { get; } = BetterWinTab.Models.Transcription.DiarizationModelCatalog.RequiredAssets;
    public IReadOnlyList<ChatProviderDescriptor> Providers { get; } = ChatProviderCatalog.Providers;
    public IReadOnlyList<ReportTemplate> Templates { get; } =
    [
        ReportTemplate.MeetingNotes,
        new ReportTemplate
        {
            Id = "action-items", Name = "Action items",
            SystemPrompt = "Extract only actions explicitly stated. Do not infer owners or deadlines.",
            UserPromptTemplate = "From the recording \"{title}\", list each action with owner and due date only when stated. Include unresolved questions.\n\nTranscript:\n{transcript}"
        },
        new ReportTemplate
        {
            Id = "decisions", Name = "Decisions and open questions",
            SystemPrompt = "Produce a factual report grounded only in the transcript.",
            UserPromptTemplate = "For \"{title}\", separate confirmed decisions, proposals not yet accepted, and open questions.\n\nTranscript:\n{transcript}"
        },
        new ReportTemplate
        {
            Id = "follow-up", Name = "Follow-up email",
            SystemPrompt = "Draft a concise, professional follow-up email. Do not invent details.",
            UserPromptTemplate = "Draft a follow-up email after \"{title}\". Include agreed actions and open questions.\n\nTranscript:\n{transcript}"
        }
    ];

    [ObservableProperty] private AudioDeviceInfo? _selectedMicrophone;
    [ObservableProperty] private bool _includeMicrophone;
    [ObservableProperty] private AudioDeviceInfo? _selectedOutput;
    [ObservableProperty] private AudioRecordingMetadata? _selectedRecording;
    [ObservableProperty] private GeneratedDocument? _selectedReport;
    [ObservableProperty] private string _providerId = "openai";
    [ObservableProperty] private string _modelId = "gpt-4o-mini";
    [ObservableProperty] private ChatModel? _selectedChatModel;
    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private string _transcriptText = string.Empty;
    [ObservableProperty] private string _reportText = string.Empty;
    [ObservableProperty] private string _selectedTemplateId = "meeting-notes";
    [ObservableProperty] private string _customPrompt = string.Empty;
    [ObservableProperty] private string _languageSearch = string.Empty;
    [ObservableProperty] private string _selectedLanguage = "Automatic";
    [ObservableProperty] private BetterWinTab.Models.Transcription.AsrModel? _selectedAsrModel;
    [ObservableProperty] private bool _isManualLanguage;
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private bool _isGeneratingReport;
    [ObservableProperty] private int _countdownSeconds;
    [ObservableProperty] private string _status = "Ready to record";
    [ObservableProperty] private string _recordingTitle = string.Empty;
    [ObservableProperty] private bool _isTranscribing;
    [ObservableProperty] private bool _isInstallingAsrModel;
    [ObservableProperty] private string _modelCatalogStatus = string.Empty;
    private bool _initializingSettings = true;

    public Visibility StopRecordingVisibility => IsRecording ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CountdownVisibility => CountdownSeconds > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TranscriptEmptyVisibility => string.IsNullOrWhiteSpace(TranscriptText) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ManualLanguageVisibility => IsManualLanguage ? Visibility.Visible : Visibility.Collapsed;
    public bool CanIncludeMicrophone => Microphones.Count > 0;
    public Visibility ProviderModelPickerVisibility => ProviderId.Equals("openrouter", StringComparison.OrdinalIgnoreCase) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility OpenRouterModelHintVisibility => ProviderModelPickerVisibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    public string DiarizationStatus => BetterWinTab.Models.Transcription.DiarizationModelCatalog.CurrentTranscriptCapability;
    public bool IsAsrModelInstalled => _asrModelStore.IsStreamingInstalled;
    public Visibility ReportEmptyVisibility => string.IsNullOrWhiteSpace(ReportText) ? Visibility.Visible : Visibility.Collapsed;
    public string TranscriptContent => !string.IsNullOrWhiteSpace(TranscriptText) ? TranscriptText : SelectedRecording?.Transcript.Count > 0
        ? string.Join(Environment.NewLine + Environment.NewLine, SelectedRecording.Transcript.Select(s =>
            $"[{TimeSpan.FromSeconds(s.StartSeconds):hh\\:mm\\:ss}] {s.Speaker ?? "Speaker"}: {s.Text}"))
        : TranscriptText;

    public CallsViewModel(AudioCaptureService capture, AudioDeviceService devices,
        AudioRecordingStoreService recordings, ReportGenerationService reports,
        GeneratedDocumentStore documents, CredentialStore credentials,
        AppSettings appSettings, SettingsService settingsService)
    {
        InitializeTranscriptPresentation();
        InitializeCallsWorkspace();
        _capture = capture;
        _devices = devices;
        _recordings = recordings;
        _reports = reports;
        _documents = documents;
        _credentials = credentials;
        _appSettings = appSettings;
        _settingsService = settingsService;
        _appSettings.Calls ??= new CallsSettings();
        IncludeMicrophone = _appSettings.Calls.IncludeMicrophone;
        ProviderId = _appSettings.Calls.ProviderId;
        ModelId = _appSettings.Calls.ModelId;
        IsManualLanguage = _appSettings.Calls.ManualLanguage;
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.AllCultures)
                     .Where(c => !string.IsNullOrEmpty(c.Name))
                     .GroupBy(c => c.ThreeLetterISOLanguageName, StringComparer.OrdinalIgnoreCase)
                     .Select(g => g.OrderByDescending(c => c.Name.Contains('-')).First())
                     .OrderBy(c => c.EnglishName, StringComparer.CurrentCultureIgnoreCase))
            Languages.Add($"{culture.EnglishName} ({culture.Name})");
        Languages.Insert(0, "Automatic");
        RefreshVisibleLanguages();
        SelectedAsrModel = AsrModels.FirstOrDefault();
        SelectedAsrModel = AsrModels.FirstOrDefault(m => m.Id == _appSettings.Calls.AsrModelId) ?? SelectedAsrModel;
        var savedMicrophoneId = _appSettings.Calls.MicrophoneDeviceId;
        var savedOutputId = _appSettings.Calls.OutputDeviceId;
        RefreshDevices();
        SelectedMicrophone = Microphones.FirstOrDefault(m => m.Id == savedMicrophoneId) ?? SelectedMicrophone;
        SelectedOutput = Outputs.FirstOrDefault(m => m.Id == savedOutputId) ?? SelectedOutput;
        SelectedLanguage = IsManualLanguage
            ? Languages.FirstOrDefault(language => language.EndsWith($"({_appSettings.Calls.Locale})", StringComparison.OrdinalIgnoreCase)) ?? "Automatic"
            : "Automatic";
        SetModelsForProvider(ProviderId);
        if (ProviderId.Equals("openai", StringComparison.OrdinalIgnoreCase) && ModelId == "gpt-4o-mini")
            ModelId = "gpt-6-luna";
        _initializingSettings = false;
        _ = LoadAsync();
        ModelCatalogStatus = ProviderId.Equals("openrouter", StringComparison.OrdinalIgnoreCase)
            ? "Enter any OpenRouter model ID directly (for example, provider/model)."
            : "Model suggestions reviewed 22 Oct 2026. Availability can vary by account; you can enter another model ID below.";
        SaveCallSettings();
    }

    private void RefreshVisibleLanguages()
    {
        var query = LanguageSearch.Trim();
        VisibleLanguages.Clear();
        foreach (var language in Languages.Where(x => query.Length == 0 || x.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
            VisibleLanguages.Add(language);
    }

    public void RefreshDevices()
    {
        Microphones.Clear();
        Outputs.Clear();
        try
        {
            foreach (var item in _devices.EnumerateMicrophones().Where(device => !device.RequiresHandsFreeProfile)) Microphones.Add(item);
            foreach (var item in _devices.EnumerateLoopbackDevices().Where(device => !device.RequiresHandsFreeProfile)) Outputs.Add(item);
        }
        catch (Exception ex) { Status = $"Audio devices are unavailable: {ex.Message}"; }
        SelectedMicrophone = Microphones.FirstOrDefault(d => d.IsDefault) ?? Microphones.FirstOrDefault();
        SelectedOutput = Outputs.FirstOrDefault(d => d.IsDefault) ?? Outputs.FirstOrDefault();
        if (!CanIncludeMicrophone) IncludeMicrophone = false;
        OnPropertyChanged(nameof(CanIncludeMicrophone));
    }

    [RelayCommand]
    private void RefreshAudioDevices()
    {
        if (!IsRecording && !_isStartingRecording) RefreshDevices();
    }

    private async Task LoadAsync()
    {
        try
        {
            var items = await _recordings.GetAllAsync();
            var docs = await _documents.ListAsync();
            await RunOnUiAsync(() =>
            {
                var selectedId = SelectedRecording?.Id;
                Recordings.Clear(); foreach (var item in items) Recordings.Add(item);
                Reports.Clear(); foreach (var item in docs) Reports.Add(item);
                SelectedRecording = Recordings.FirstOrDefault(r => r.Id == selectedId) ?? Recordings.FirstOrDefault();
            });
        }
        catch (Exception ex) { Status = $"Could not load recordings: {ex.Message}"; }
    }

    [RelayCommand]
    private void CancelCountdown()
    {
        _captureLifetime?.Cancel();
        CountdownSeconds = 0;
    }

    [RelayCommand]
    private async Task GenerateReportAsync()
    {
        if (SelectedRecording == null || IsGeneratingReport) return;
        var transcript = TranscriptContent;
        var recordingId = SelectedRecording.Id;
        var title = RecordingTitle;
        var providerId = ProviderId;
        var modelId = ModelId;
        if (string.IsNullOrWhiteSpace(transcript))
        {
            Status = "Add or transcribe the recording before generating a report.";
            return;
        }
        try
        {
            IsGeneratingReport = true;
            _generatingRecordingId = recordingId;
            _draftReport = string.Empty;
            SelectedReport = null;
            ReportText = string.Empty;
            Status = "Generating notes…";
            if (!string.IsNullOrWhiteSpace(ApiKey))
                _credentials.SetApiKey(ProviderId, ApiKey);
            var template = Templates.FirstOrDefault(t => t.Id == SelectedTemplateId) ?? ReportTemplate.MeetingNotes;
            if (!string.IsNullOrWhiteSpace(CustomPrompt))
                template = template with
                {
                    UserPromptTemplate = template.UserPromptTemplate + "\n\nAdditional instructions: " + CustomPrompt.Trim()
                };
            template = template with { SystemPrompt = template.SystemPrompt + " Format the response as Markdown with clear headings and lists where useful. Do not wrap the entire response in a code fence." };
            var progress = new Progress<string>(delta => _ = RunOnUiAsync(() =>
            {
                if (_generatingRecordingId != recordingId) return;
                _draftReport += delta;
                if (SelectedRecording?.Id == recordingId) ReportText = _draftReport;
            }));
            var generated = (await _reports.GenerateAsync(title, transcript, template, providerId, modelId, progress))
                with { RecordingId = recordingId };
            await _documents.SaveAsync(generated);
            _generatingRecordingId = null;
            Reports.Insert(0, generated);
            if (SelectedRecording?.Id == recordingId)
            {
                SelectedReport = generated;
                ReportText = generated.Content;
            }
            Status = "Report generated and saved.";
        }
        catch (Exception ex) { Status = $"Report generation failed: {ex.Message}"; }
        finally { _generatingRecordingId = null; IsGeneratingReport = false; }
    }

    partial void OnSelectedRecordingChanged(AudioRecordingMetadata? value)
    {
        LoadTranscriptMessages(value?.Transcript ?? Enumerable.Empty<AudioTranscriptSegment>());
        TranscriptText = value?.Transcript.Count > 0
            ? TranscriptTextEditor.Format(value.Transcript)
            : string.Empty;
        OnPropertyChanged(nameof(TranscriptContent));
        RecordingTitle = value == null ? string.Empty : $"Recording {value.StartedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
        RefreshCurrentReports();
        var report = Reports.FirstOrDefault(d => d.RecordingId == value?.Id);
        SelectedReport = report;
        ReportText = _generatingRecordingId == value?.Id && value != null ? _draftReport : report?.Content ?? string.Empty;
    }

    partial void OnSelectedReportChanged(GeneratedDocument? value) => ReportText = value?.Content ?? string.Empty;
    partial void OnIsRecordingChanged(bool value) => OnPropertyChanged(nameof(StopRecordingVisibility));
    partial void OnCountdownSecondsChanged(int value) => OnPropertyChanged(nameof(CountdownVisibility));
    partial void OnTranscriptTextChanged(string value) { OnPropertyChanged(nameof(TranscriptContent)); OnPropertyChanged(nameof(TranscriptEmptyVisibility)); }
    partial void OnReportTextChanged(string value) => OnPropertyChanged(nameof(ReportEmptyVisibility));
    partial void OnLanguageSearchChanged(string value) => RefreshVisibleLanguages();

    partial void OnSelectedMicrophoneChanged(AudioDeviceInfo? value) { _appSettings.Calls.MicrophoneDeviceId = value?.Id; SaveCallSettings(); }
    partial void OnIncludeMicrophoneChanged(bool value) { _appSettings.Calls.IncludeMicrophone = value; SaveCallSettings(); }
    partial void OnSelectedOutputChanged(AudioDeviceInfo? value) { _appSettings.Calls.OutputDeviceId = value?.Id; SaveCallSettings(); }
    partial void OnSelectedAsrModelChanged(BetterWinTab.Models.Transcription.AsrModel? value)
    { if (value != null) _appSettings.Calls.AsrModelId = value.Id; SaveCallSettings(); }
    partial void OnIsManualLanguageChanged(bool value)
    { OnPropertyChanged(nameof(ManualLanguageVisibility)); _appSettings.Calls.ManualLanguage = value; SaveCallSettings(); }
    partial void OnSelectedLanguageChanged(string value)
    {
        var open = value.LastIndexOf('(');
        var locale = open >= 0 && value.EndsWith(')') ? value[(open + 1)..^1] : "es-ES";
        _appSettings.Calls.Locale = locale;
        SaveCallSettings();
    }
    partial void OnProviderIdChanged(string value)
    {
        OnPropertyChanged(nameof(ProviderModelPickerVisibility));
        OnPropertyChanged(nameof(OpenRouterModelHintVisibility));
        _appSettings.Calls.ProviderId = value;
        SaveCallSettings();
        SetModelsForProvider(value);
        if (value.Equals("openrouter", StringComparison.OrdinalIgnoreCase))
            ModelCatalogStatus = "Enter any OpenRouter model ID directly (for example, provider/model).";
        else
        {
            ModelCatalogStatus = "Model suggestions reviewed 22 Oct 2026. Availability can vary by account; you can enter another model ID below.";
            if (!_initializingSettings && ChatModels.Count > 0)
                ModelId = ChatModels[0].Id;
        }
    }
    partial void OnModelIdChanged(string value)
    {
        _appSettings.Calls.ModelId = value;
        SaveCallSettings();
        SelectedChatModel = ChatModels.FirstOrDefault(model => model.Id.Equals(value, StringComparison.OrdinalIgnoreCase));
    }
    partial void OnSelectedChatModelChanged(ChatModel? value)
    {
        if (value != null && !value.Id.Equals(ModelId, StringComparison.Ordinal))
            ModelId = value.Id;
    }

    private void SaveCallSettings()
    {
        if (!_initializingSettings) _settingsService.Save(_appSettings);
    }

    private void SetModelsForProvider(string providerId)
    {
        ChatModels.Clear();
        foreach (var model in ChatProviderCatalog.GetModels(providerId))
            ChatModels.Add(model);
        SelectedChatModel = ChatModels.FirstOrDefault(model => model.Id.Equals(ModelId, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    private async Task ImportAsrModelAsync()
    {
        if (IsRecording || IsTranscribing || IsInstallingAsrModel || CountdownSeconds > 0) return;
        var model = SelectedAsrModel ?? AsrModels[0];
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".zip");
        picker.FileTypeFilter.Add(".bz2");
        InitializeWithWindow.Initialize(picker, App.Current.Hwnd);
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;
        try
        {
            IsInstallingAsrModel = true;
            Status = "Installing the local ASR model…";
            await _asrModelStore.InstallArchiveAsync(model, file.Path);
            await _asrModelStore.EnsureStreamingModelsAsync(new Progress<string>(message => Status = message));
            Status = "Speech and speaker models are ready.";
        }
        catch (Exception ex) { Status = $"Could not install ASR model: {ex.Message}"; }
        finally { IsInstallingAsrModel = false; OnPropertyChanged(nameof(IsAsrModelInstalled)); }
    }

    [RelayCommand]
    private async Task DownloadAsrModelAsync()
    {
        if (IsRecording || IsTranscribing || IsInstallingAsrModel || CountdownSeconds > 0) return;
        try
        {
            await PrepareSpeechModelsAsync(CancellationToken.None);
            Status = "Speech and speaker models are ready. Start a recording to see words as they are recognized.";
        }
        catch (Exception ex) { Status = $"Could not download or install the speech model: {ex.Message}"; }
    }

    private Task RunOnUiAsync(Action action)
    {
        if (_dispatcher == null || _dispatcher.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(() =>
            {
                try { action(); completion.SetResult(); }
                catch (Exception ex) { completion.SetException(ex); }
            }))
            completion.SetException(new InvalidOperationException("The recording window is no longer available."));
        return completion.Task;
    }

    [RelayCommand]
    private async Task TranscribeSelectedRecordingAsync()
    {
        var recording = SelectedRecording;
        if (recording == null || !CanTranscribeRecording || IsInstallingAsrModel || _isFinalizingRecording) return;
        if (!IsAsrModelInstalled)
        {
            await DownloadAsrModelAsync();
            if (!IsAsrModelInstalled) return;
        }
        await TranscribeRecordingAsync(recording.Id);
    }

    [RelayCommand]
    private void SaveApiKey()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            Status = "Enter an API key to save it securely.";
            return;
        }
        _credentials.SetApiKey(ProviderId, ApiKey);
        ApiKey = string.Empty;
        Status = $"{ProviderId} API key saved in Windows Credential Locker.";
    }

    [RelayCommand]
    private async Task SaveTranscriptAsync()
    {
        if (SelectedRecording == null || IsRecording || IsTranscribing || _isFinalizingRecording || string.IsNullOrWhiteSpace(TranscriptText)) return;
        var savedText = TranscriptTextEditor.Format(SelectedRecording.Transcript);
        if (TranscriptText == savedText) return;
        var segments = TranscriptTextEditor.ApplyEdit(SelectedRecording.Transcript, TranscriptText);
        await _recordings.SetTranscriptAsync(SelectedRecording.Id, segments);
        var refreshed = await _recordings.GetAsync(SelectedRecording.Id);
        if (refreshed != null) SelectedRecording = refreshed;
        Status = "Transcript saved.";
    }

    [RelayCommand]
    private async Task DeleteRecordingAsync()
    {
        if (SelectedRecording == null || IsTranscribing || _isFinalizingRecording || SelectedRecording.Id == _activeRecordingId) return;
        var item = SelectedRecording;
        await _recordings.DeleteAsync(item.Id);
        foreach (var path in new[] { item.MicrophoneAudioFilePath, item.LoopbackAudioFilePath }.Where(p => !string.IsNullOrWhiteSpace(p)))
            try { File.Delete(path!); } catch (IOException) { }
        Recordings.Remove(item);
        SelectedRecording = Recordings.FirstOrDefault();
    }

    public async ValueTask DisposeAsync()
    {
        if (_capture.IsCapturing)
            await StopRecordingAsync();
        else
            _captureLifetime?.Cancel();
    }
}
