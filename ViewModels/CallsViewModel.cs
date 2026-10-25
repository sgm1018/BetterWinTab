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
    private readonly TranscriptionService _transcription;
    private readonly DispatcherQueue? _dispatcher = DispatcherQueue.GetForCurrentThread();
    private CancellationTokenSource? _captureLifetime;
    private string? _activeRecordingId;
    private Channel<LiveAudioChunk>? _liveAudioChunks;
    private Task? _liveAudioProcessor;
    private Task? _microphoneChunkProducer;
    private Task? _loopbackChunkProducer;
    private readonly List<AudioTranscriptSegment> _liveTranscriptSegments = new();
    private readonly object _liveTranscriptLock = new();
    private int _liveTranscriptionHadErrors;

    public ObservableCollection<AudioDeviceInfo> Microphones { get; } = new();
    public ObservableCollection<AudioDeviceInfo> Outputs { get; } = new();
    public ObservableCollection<AudioRecordingMetadata> Recordings { get; } = new();
    public ObservableCollection<GeneratedDocument> Reports { get; } = new();
    public ObservableCollection<string> Languages { get; } = new();
    public ObservableCollection<string> VisibleLanguages { get; } = new();
    public ObservableCollection<ChatModel> ChatModels { get; } = new();
    public IReadOnlyList<BetterWinTab.Models.Transcription.AsrModel> AsrModels { get; } = BetterWinTab.Models.Transcription.AsrModelCatalog.Models;
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
    public Visibility ProviderModelPickerVisibility => ProviderId.Equals("openrouter", StringComparison.OrdinalIgnoreCase) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility OpenRouterModelHintVisibility => ProviderModelPickerVisibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    public string DiarizationStatus => BetterWinTab.Models.Transcription.DiarizationModelCatalog.CurrentTranscriptCapability;
    public bool IsAsrModelInstalled => _asrModelStore.IsInstalled(AsrModels.First(m => m.Id == "moonshine-v2-base-es"));
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
        _capture = capture;
        _devices = devices;
        _recordings = recordings;
        _reports = reports;
        _documents = documents;
        _credentials = credentials;
        _appSettings = appSettings;
        _settingsService = settingsService;
        _transcription = new TranscriptionService([new SherpaOfflineWorkerEngine(_asrModelStore)]);
        _appSettings.Calls ??= new CallsSettings();
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
        RefreshDevices();
        SelectedMicrophone = Microphones.FirstOrDefault(m => m.Id == _appSettings.Calls.MicrophoneDeviceId) ?? SelectedMicrophone;
        SelectedOutput = Outputs.FirstOrDefault(m => m.Id == _appSettings.Calls.OutputDeviceId) ?? SelectedOutput;
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
            foreach (var item in _devices.EnumerateMicrophones()) Microphones.Add(item);
            foreach (var item in _devices.EnumerateLoopbackDevices()) Outputs.Add(item);
        }
        catch (Exception ex) { Status = $"Audio devices are unavailable: {ex.Message}"; }
        SelectedMicrophone = Microphones.FirstOrDefault(d => d.IsDefault) ?? Microphones.FirstOrDefault();
        SelectedOutput = Outputs.FirstOrDefault(d => d.IsDefault) ?? Outputs.FirstOrDefault();
    }

    private async Task LoadAsync()
    {
        try
        {
            var items = await _recordings.GetAllAsync();
            var docs = await _documents.ListAsync();
            _dispatcher?.TryEnqueue(() =>
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
    private async Task CreateRecordingAsync()
    {
        if (IsRecording || CountdownSeconds > 0) return;
        _captureLifetime = new CancellationTokenSource();
        try
        {
            for (var remaining = 3; remaining > 0; remaining--)
            {
                CountdownSeconds = remaining;
                await Task.Delay(1000, _captureLifetime.Token);
            }
            CountdownSeconds = 0;
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BetterWinTab", "recordings", Guid.NewGuid().ToString("N"));
            await _capture.StartAsync(new AudioCaptureOptions
            {
                MicrophoneDeviceId = SelectedMicrophone?.Id,
                LoopbackDeviceId = SelectedOutput?.Id,
                RecordingDirectory = directory
            }, _captureLifetime.Token);
            var metadata = await _recordings.CreateAsync(SelectedMicrophone?.Id, SelectedOutput?.Id,
                cancellationToken: _captureLifetime.Token,
                microphoneAudioFilePath: _capture.MicrophoneRecordingPath,
                loopbackAudioFilePath: _capture.LoopbackRecordingPath);
            _activeRecordingId = metadata.Id;
            RecordingTitle = $"Recording {metadata.StartedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
            IsRecording = true;
            StartLiveTranscription(metadata.Id, directory, _captureLifetime.Token);
            Recordings.Insert(0, metadata);
            SelectedRecording = metadata;
        }
        catch (OperationCanceledException) { Status = "Recording cancelled."; }
        catch (Exception ex)
        {
            if (_capture.IsCapturing) await _capture.StopAsync();
            CountdownSeconds = 0;
            Status = $"Could not start recording: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task StopRecordingAsync()
    {
        if (_capture.IsCapturing) await _capture.StopAsync();
        var completedRecordingId = _activeRecordingId;
        IsRecording = false;
        CountdownSeconds = 0;
        Exception? liveCaptureError = null;
        if (_microphoneChunkProducer != null && _loopbackChunkProducer != null)
        {
            try { await Task.WhenAll(_microphoneChunkProducer, _loopbackChunkProducer); }
            catch (Exception ex) { liveCaptureError = ex; }
            finally { _liveAudioChunks?.Writer.TryComplete(); }
            if (_liveAudioProcessor != null)
            {
                try { await _liveAudioProcessor; }
                catch (Exception ex) { liveCaptureError ??= ex; }
            }
            IsTranscribing = false;
        }
        if (completedRecordingId != null)
        {
            await _recordings.CompleteAsync(completedRecordingId);
            _activeRecordingId = null;
        }
        lock (_liveTranscriptLock)
            _liveTranscriptSegments.Clear();
        Status = _capture.RecordingWriteError == null ? "Recording saved" : $"Audio saved with a write warning: {_capture.RecordingWriteError.Message}";
        await LoadAsync();
        if (completedRecordingId != null)
        {
            if (SelectedAsrModel?.Id != "moonshine-v2-base-es")
                Status = "Recording saved. The selected model has no configured worker; choose Moonshine Spanish Base.";
            else if (IsAsrModelInstalled)
            {
                if (_liveAudioProcessor == null || liveCaptureError != null || Volatile.Read(ref _liveTranscriptionHadErrors) != 0)
                    await TranscribeRecordingAsync(completedRecordingId);
                else
                {
                    var saved = await _recordings.GetAsync(completedRecordingId);
                    Status = saved?.Transcript.Count > 0
                        ? "Recording saved. Live transcription is complete."
                        : "Recording saved. No speech was recognized in the selected microphone or system audio.";
                }
            }
            else
                Status = "Recording saved with both audio tracks. Download or import Moonshine Spanish Base to transcribe it; speech was not transcribed during this recording.";
        }
        _liveAudioChunks = null;
        _liveAudioProcessor = null;
        _microphoneChunkProducer = null;
        _loopbackChunkProducer = null;
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
        if (string.IsNullOrWhiteSpace(transcript))
        {
            Status = "Add or transcribe the recording before generating a report.";
            return;
        }
        try
        {
            IsGeneratingReport = true;
            if (!string.IsNullOrWhiteSpace(ApiKey))
                _credentials.SetApiKey(ProviderId, ApiKey);
            var template = Templates.FirstOrDefault(t => t.Id == SelectedTemplateId) ?? ReportTemplate.MeetingNotes;
            if (!string.IsNullOrWhiteSpace(CustomPrompt))
                template = template with
                {
                    Id = "custom",
                    Name = "Custom request",
                    SystemPrompt = "Create a useful report grounded only in the provided transcript. Do not invent details.",
                    UserPromptTemplate = CustomPrompt.Trim() + "\n\nRecording: {title}\n\nTranscript:\n{transcript}"
                };
            var generated = (await _reports.GenerateAsync(RecordingTitle, transcript, template, ProviderId, ModelId))
                with { RecordingId = SelectedRecording.Id };
            await _documents.SaveAsync(generated);
            Reports.Insert(0, generated);
            SelectedReport = generated;
            ReportText = generated.Content;
            Status = "Report generated and saved.";
        }
        catch (Exception ex) { Status = $"Report generation failed: {ex.Message}"; }
        finally { IsGeneratingReport = false; }
    }

    partial void OnSelectedRecordingChanged(AudioRecordingMetadata? value)
    {
        TranscriptText = value?.Transcript.Count > 0
            ? string.Join(Environment.NewLine + Environment.NewLine, value.Transcript.Select(s => $"{s.Speaker ?? "Speaker"}: {s.Text}"))
            : string.Empty;
        OnPropertyChanged(nameof(TranscriptContent));
        RecordingTitle = value == null ? string.Empty : $"Recording {value.StartedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
        var report = Reports.FirstOrDefault(d => d.RecordingId == value?.Id);
        SelectedReport = report;
        ReportText = report?.Content ?? string.Empty;
    }

    partial void OnSelectedReportChanged(GeneratedDocument? value) => ReportText = value?.Content ?? string.Empty;
    partial void OnIsRecordingChanged(bool value) => OnPropertyChanged(nameof(StopRecordingVisibility));
    partial void OnCountdownSecondsChanged(int value) => OnPropertyChanged(nameof(CountdownVisibility));
    partial void OnTranscriptTextChanged(string value) { OnPropertyChanged(nameof(TranscriptContent)); OnPropertyChanged(nameof(TranscriptEmptyVisibility)); }
    partial void OnReportTextChanged(string value) => OnPropertyChanged(nameof(ReportEmptyVisibility));
    partial void OnLanguageSearchChanged(string value) => RefreshVisibleLanguages();

    partial void OnSelectedMicrophoneChanged(AudioDeviceInfo? value) { _appSettings.Calls.MicrophoneDeviceId = value?.Id; SaveCallSettings(); }
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
        var model = SelectedAsrModel ?? AsrModels.First(m => m.Id == "moonshine-v2-base-es");
        if (model.Id != "moonshine-v2-base-es")
        {
            Status = "Only Moonshine Spanish Base can be installed by this worker.";
            return;
        }
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".zip");
        picker.FileTypeFilter.Add(".bz2");
        InitializeWithWindow.Initialize(picker, App.Current.Hwnd);
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;
        try
        {
            Status = "Installing the local ASR model…";
            await _asrModelStore.InstallArchiveAsync(model, file.Path);
            OnPropertyChanged(nameof(IsAsrModelInstalled));
            if (SelectedRecording?.EndedAtUtc != null)
                await TranscribeRecordingAsync(SelectedRecording.Id);
            else
                Status = "Moonshine Spanish Base is installed. Transcription will run after recording stops.";
        }
        catch (Exception ex) { Status = $"Could not install ASR model: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task DownloadAsrModelAsync()
    {
        var model = SelectedAsrModel;
        if (model == null) return;
        if (model.Id != "moonshine-v2-base-es")
        {
            Status = "Only Moonshine Spanish Base has a verified download and configured worker.";
            return;
        }
        try
        {
            IsInstallingAsrModel = true;
            Status = $"Downloading and installing {model.DisplayName}… This model is about {model.ApproximateDownloadSize}.";
            await _asrModelStore.DownloadAndInstallAsync(model);
            OnPropertyChanged(nameof(IsAsrModelInstalled));
            Status = $"{model.DisplayName} is ready. New recordings will show live transcription.";
            if (SelectedRecording?.EndedAtUtc != null)
                await TranscribeRecordingAsync(SelectedRecording.Id);
        }
        catch (Exception ex) { Status = $"Could not download or install the speech model: {ex.Message}"; }
        finally { IsInstallingAsrModel = false; }
    }

    private async Task TranscribeRecordingAsync(string recordingId)
    {
        var item = await _recordings.GetAsync(recordingId);
        if (item == null) return;
        var options = new TranscriptionOptions(IsManualLanguage ? LanguageSelectionMode.Manual : LanguageSelectionMode.Automatic,
            IsManualLanguage ? _appSettings.Calls.Locale : null);
        var segments = new List<AudioTranscriptSegment>();
        var failures = new List<string>();
        IsTranscribing = true;
        try
        {
            foreach (var track in new[]
                     {
                         (Path: item.MicrophoneAudioFilePath, Speaker: "You"),
                         (Path: item.LoopbackAudioFilePath, Speaker: "Call audio")
                     })
            {
                if (string.IsNullOrWhiteSpace(track.Path) || !File.Exists(track.Path)) continue;
                Status = $"Transcribing {track.Speaker} audio offline…";
                try
                {
                    var transcript = await _transcription.TranscribeWaveFileAsync("moonshine-v2-base-es", track.Path, options);
                    segments.AddRange(transcript.Segments.Select(segment => new AudioTranscriptSegment
                    {
                        StartSeconds = segment.Start.TotalSeconds,
                        DurationSeconds = segment.End.TotalSeconds - segment.Start.TotalSeconds,
                        Speaker = track.Speaker,
                        Text = segment.Text
                    }));
                }
                catch (Exception ex) { failures.Add($"{track.Speaker}: {ex.Message}"); }
            }

            if (segments.Count == 0)
            {
                Status = "No speech was returned for the recorded tracks.";
                return;
            }
            await _recordings.SetTranscriptAsync(item.Id, segments);
            var updated = await _recordings.GetAsync(item.Id);
            if (updated != null) SelectedRecording = updated;
            TranscriptText = string.Join(Environment.NewLine + Environment.NewLine,
                segments.Select(segment => $"{segment.Speaker}: {segment.Text}"));
            Status = failures.Count > 0
                ? $"Some tracks could not be transcribed: {string.Join("; ", failures)}"
                : "Offline transcription saved. Tracks are labeled by source; callers within call audio are not diarized.";
        }
        catch (Exception ex) { Status = $"Offline transcription failed: {ex.Message}"; }
        finally { IsTranscribing = false; }
    }

    private void StartLiveTranscription(string recordingId, string recordingDirectory, CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _liveTranscriptionHadErrors, 0);
        lock (_liveTranscriptLock)
            _liveTranscriptSegments.Clear();

        if (!IsAsrModelInstalled)
        {
            _microphoneChunkProducer = DrainPacketsAsync(_capture.ReadMicrophonePacketsAsync(cancellationToken), cancellationToken);
            _loopbackChunkProducer = DrainPacketsAsync(_capture.ReadLoopbackPacketsAsync(cancellationToken), cancellationToken);
            Status = "Recording microphone and system audio. Install Moonshine Spanish Base from this panel to enable live transcription.";
            return;
        }

        var microphoneFormat = _capture.MicrophoneWaveFormat;
        var loopbackFormat = _capture.LoopbackWaveFormat;
        if (microphoneFormat == null || loopbackFormat == null)
            throw new InvalidOperationException("The selected audio devices did not expose a recording format.");

        _liveAudioChunks = Channel.CreateUnbounded<LiveAudioChunk>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _liveAudioProcessor = ProcessLiveAudioChunksAsync(_liveAudioChunks.Reader, recordingId, cancellationToken);
        IsTranscribing = true;

        _microphoneChunkProducer = CaptureLiveChunksAsync(_capture.ReadMicrophonePacketsAsync(cancellationToken),
            microphoneFormat, AudioCaptureSource.Microphone, recordingDirectory, _liveAudioChunks.Writer, cancellationToken);
        _loopbackChunkProducer = CaptureLiveChunksAsync(_capture.ReadLoopbackPacketsAsync(cancellationToken),
            loopbackFormat, AudioCaptureSource.Loopback, recordingDirectory, _liveAudioChunks.Writer, cancellationToken);
        Status = "Recording microphone and system audio. Live transcription updates every 8 seconds.";
    }

    private static async Task DrainPacketsAsync(IAsyncEnumerable<AudioCapturePacket> packets, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var _ in packets.WithCancellation(cancellationToken).ConfigureAwait(false)) { }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task CaptureLiveChunksAsync(IAsyncEnumerable<AudioCapturePacket> packets, WaveFormat format,
        AudioCaptureSource source, string directory, ChannelWriter<LiveAudioChunk> writer, CancellationToken cancellationToken)
    {
        var chunker = new AudioChunkBuffer(format.AverageBytesPerSecond, chunkSeconds: 8, blockAlign: format.BlockAlign);
        var speaker = source == AudioCaptureSource.Microphone ? "You" : "Call audio";
        var chunkIndex = 0;
        var startSeconds = 0d;
        await foreach (var packet in packets.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            foreach (var bytes in chunker.Push(packet.Data))
                await QueueLiveChunkAsync(bytes, format, source, speaker, directory, writer, chunkIndex++, startSeconds, cancellationToken);
            startSeconds = chunkIndex * 8d;
        }

        var remainder = chunker.Flush();
        if (remainder is { Length: > 0 })
            await QueueLiveChunkAsync(remainder, format, source, speaker, directory, writer, chunkIndex, startSeconds, cancellationToken);
    }

    private static async Task QueueLiveChunkAsync(byte[] bytes, WaveFormat format, AudioCaptureSource source, string speaker,
        string directory, ChannelWriter<LiveAudioChunk> writer, int index, double startSeconds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.Combine(directory, $"live-{source.ToString().ToLowerInvariant()}-{index:D6}.wav");
        using (var wave = new WaveFileWriter(path, format))
            wave.Write(bytes, 0, bytes.Length);
        var duration = bytes.Length / (double)format.AverageBytesPerSecond;
        await writer.WriteAsync(new LiveAudioChunk(path, speaker, startSeconds, duration), cancellationToken).ConfigureAwait(false);
    }

    private async Task ProcessLiveAudioChunksAsync(ChannelReader<LiveAudioChunk> reader, string recordingId, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var chunk in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    var options = new TranscriptionOptions(IsManualLanguage ? LanguageSelectionMode.Manual : LanguageSelectionMode.Automatic,
                        IsManualLanguage ? _appSettings.Calls.Locale : null);
                    var transcript = await _transcription.TranscribeWaveFileAsync("moonshine-v2-base-es", chunk.Path, options, cancellationToken);
                    var newSegments = transcript.Segments
                        .Where(segment => !string.IsNullOrWhiteSpace(segment.Text))
                        .Select(segment => new AudioTranscriptSegment
                        {
                            StartSeconds = chunk.StartSeconds + segment.Start.TotalSeconds,
                            DurationSeconds = segment.End.TotalSeconds - segment.Start.TotalSeconds,
                            Speaker = chunk.Speaker,
                            Text = segment.Text.Trim()
                        }).ToArray();
                    if (newSegments.Length == 0) continue;

                    AudioTranscriptSegment[] snapshot;
                    lock (_liveTranscriptLock)
                    {
                        _liveTranscriptSegments.AddRange(newSegments);
                        snapshot = _liveTranscriptSegments.OrderBy(segment => segment.StartSeconds).ToArray();
                    }
                    await _recordings.SetTranscriptAsync(recordingId, snapshot, cancellationToken);
                    var text = string.Join(Environment.NewLine + Environment.NewLine,
                        snapshot.Select(segment => $"{segment.Speaker}: {segment.Text}"));
                    _dispatcher?.TryEnqueue(() =>
                    {
                        if (SelectedRecording?.Id == recordingId)
                            TranscriptText = text;
                        if (_activeRecordingId == recordingId)
                            Status = $"Recording and transcribing live. {snapshot.Length} speech segment(s) added.";
                    });
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    Interlocked.Exchange(ref _liveTranscriptionHadErrors, 1);
                    _dispatcher?.TryEnqueue(() => Status = $"Recording continues, but a live audio chunk could not be transcribed: {ex.Message}");
                }
                finally
                {
                    try { File.Delete(chunk.Path); } catch (IOException) { }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private sealed record LiveAudioChunk(string Path, string Speaker, double StartSeconds, double DurationSeconds);

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
        if (SelectedRecording == null || string.IsNullOrWhiteSpace(TranscriptText)) return;
        var segments = new[] { new AudioTranscriptSegment
        {
            StartSeconds = 0,
            DurationSeconds = 0,
            Text = TranscriptText.Trim()
        } };
        await _recordings.SetTranscriptAsync(SelectedRecording.Id, segments);
        var refreshed = await _recordings.GetAsync(SelectedRecording.Id);
        if (refreshed != null) SelectedRecording = refreshed;
        Status = "Transcript saved.";
    }

    [RelayCommand]
    private async Task DeleteRecordingAsync()
    {
        if (SelectedRecording == null || SelectedRecording.Id == _activeRecordingId) return;
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
