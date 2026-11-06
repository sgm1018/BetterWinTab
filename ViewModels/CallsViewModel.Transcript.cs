using System.Collections.ObjectModel;
using BetterWinTab.Models;
using BetterWinTab.Models.Transcription;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;

namespace BetterWinTab.ViewModels;

public partial class CallsViewModel
{
    private readonly HashSet<(string Source, string Id)> _confirmedTranscriptTurns = new();
    public ObservableCollection<TranscriptMessageViewModel> TranscriptMessages { get; } = new();
    public event EventHandler? TranscriptPresentationChanged;
    [ObservableProperty] private bool _isEditingTranscript;

    public bool CanEditTranscript => SelectedRecording != null && !IsRecording && !IsTranscribing && CountdownSeconds == 0;
    public bool CanTranscribeRecording => CanEditTranscript && SelectedRecording is { TranscriptionStarted: false, Transcript.Count: 0 };
    public Visibility ConversationVisibility => IsEditingTranscript ? Visibility.Collapsed : Visibility.Visible;
    public Visibility TranscriptEditorVisibility => IsEditingTranscript ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ConversationEmptyVisibility => TranscriptMessages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public string TranscriptEditLabel => IsEditingTranscript ? "Done editing" : "Edit transcript";

    private void InitializeTranscriptPresentation()
    {
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not (nameof(IsRecording) or nameof(IsTranscribing) or nameof(SelectedRecording) or nameof(CountdownSeconds))) return;
            if (!CanEditTranscript || args.PropertyName == nameof(SelectedRecording)) IsEditingTranscript = false;
            OnPropertyChanged(nameof(CanEditTranscript));
            OnPropertyChanged(nameof(CanTranscribeRecording));
            ToggleTranscriptEditingCommand.NotifyCanExecuteChanged();
        };
    }

    [RelayCommand(CanExecute = nameof(CanEditTranscript))]
    private async Task ToggleTranscriptEditingAsync()
    {
        if (!CanEditTranscript) return;
        if (IsEditingTranscript)
        {
            if (SaveTranscriptCommand.IsRunning && SaveTranscriptCommand.ExecutionTask != null)
                await SaveTranscriptCommand.ExecutionTask;
            else await SaveTranscriptCommand.ExecuteAsync(null);
            IsEditingTranscript = false;
        }
        else IsEditingTranscript = true;
    }

    partial void OnIsEditingTranscriptChanged(bool value)
    {
        OnPropertyChanged(nameof(ConversationVisibility));
        OnPropertyChanged(nameof(TranscriptEditorVisibility));
        OnPropertyChanged(nameof(TranscriptEditLabel));
    }

    public void ApplyStreamingUpdate(StreamingTranscriptEvent update)
    {
        if (update.Type == "partial")
        {
            if (_confirmedTranscriptTurns.Contains((update.Source, update.SegmentId))) return;
            var row = TranscriptMessages.FirstOrDefault(m => m.Source == update.Source && m.IsProvisional);
            if (row == null)
            {
                row = new TranscriptMessageViewModel();
                TranscriptMessages.Add(row);
            }
            row.Update(update.SegmentId, update.Source, update.StartSeconds, update.Text, null, null, true);
            PlaceTranscriptRow(row);
        }
        else if (update.Type == "final")
        {
            var parent = TranscriptMessages.FirstOrDefault(m => m.Source == update.Source && m.Id == update.SegmentId && m.IsProvisional);
            if (parent != null) TranscriptMessages.Remove(parent);
            _confirmedTranscriptTurns.Add((update.Source, update.SegmentId));
            var segments = update.Segments ??
                [new StreamingTranscriptSegment(update.SegmentId, update.Source, update.StartSeconds, update.DurationSeconds, update.Text)];
            foreach (var segment in segments)
            {
                var source = string.IsNullOrEmpty(segment.Source) ? update.Source : segment.Source;
                var row = TranscriptMessages.FirstOrDefault(m => m.Source == source && m.Id == segment.Id && !m.IsProvisional);
                if (row == null)
                {
                    row = new TranscriptMessageViewModel();
                    TranscriptMessages.Add(row);
                }
                row.Update(segment.Id, source, segment.StartSeconds, segment.Text, segment.SpeakerId, segment.Speaker, false,
                    uncertain: segment.IsSpeakerUncertain);
                PlaceTranscriptRow(row);
            }
        }
        else return;
        NotifyTranscriptPresentationChanged();
    }

    public void LoadTranscriptMessages(IEnumerable<AudioTranscriptSegment> segments)
    {
        TranscriptMessages.Clear();
        _confirmedTranscriptTurns.Clear();
        IsEditingTranscript = false;
        var index = 0;
        foreach (var segment in segments.OrderBy(s => s.StartSeconds))
        {
            var row = new TranscriptMessageViewModel();
            row.Update(segment.Id ?? $"legacy-{index++}", segment.Source ?? string.Empty, segment.StartSeconds,
                segment.Text, segment.SpeakerId, segment.Speaker, false, segment.IsIncomplete, segment.IsSpeakerUncertain);
            TranscriptMessages.Add(row);
        }
        NotifyTranscriptPresentationChanged();
    }

    public void ClearTranscriptMessages()
    {
        TranscriptMessages.Clear();
        _confirmedTranscriptTurns.Clear();
        IsEditingTranscript = false;
        NotifyTranscriptPresentationChanged();
    }

    private void PlaceTranscriptRow(TranscriptMessageViewModel row)
    {
        var oldIndex = TranscriptMessages.IndexOf(row);
        var newIndex = TranscriptMessages.Where((message, index) => !ReferenceEquals(message, row)
            && (message.StartSeconds < row.StartSeconds || (message.StartSeconds == row.StartSeconds && index < oldIndex))).Count();
        if (newIndex != oldIndex) TranscriptMessages.Move(oldIndex, newIndex);
    }

    private void NotifyTranscriptPresentationChanged()
    {
        OnPropertyChanged(nameof(ConversationEmptyVisibility));
        TranscriptPresentationChanged?.Invoke(this, EventArgs.Empty);
    }
}
