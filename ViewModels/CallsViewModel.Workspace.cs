using BetterWinTab.Models.Calls;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;

namespace BetterWinTab.ViewModels;

public partial class CallsViewModel
{
    private string? _generatingRecordingId;
    private string _draftReport = string.Empty;

    public ObservableCollection<GeneratedDocument> CurrentReports { get; } = new();
    public bool CanConfigureAudio => !IsRecording && !IsTranscribing && !IsInstallingAsrModel && CountdownSeconds == 0;
    public bool CanConfigureAi => !IsGeneratingReport;
    public bool CanGenerateNotes => SelectedRecording != null && !string.IsNullOrWhiteSpace(TranscriptContent) && !IsGeneratingReport;
    public bool CanCopyNotes => !string.IsNullOrWhiteSpace(ReportText);
    public Visibility StartRecordingVisibility => IsRecording ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ReportContentVisibility => string.IsNullOrWhiteSpace(ReportText) ? Visibility.Collapsed : Visibility.Visible;
    public string GenerateNotesLabel => IsGeneratingReport ? "Generating…" : "Generate notes";
    public string WorkspaceTitle => string.IsNullOrWhiteSpace(RecordingTitle) ? "Your next conversation" : RecordingTitle;

    private void InitializeCallsWorkspace()
    {
        Reports.CollectionChanged += (_, _) => RefreshCurrentReports();
        PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(SelectedRecording):
                    OnPropertyChanged(nameof(CanGenerateNotes));
                    break;
                case nameof(TranscriptContent):
                    OnPropertyChanged(nameof(CanGenerateNotes));
                    break;
                case nameof(IsRecording):
                    OnPropertyChanged(nameof(StartRecordingVisibility));
                    goto case nameof(IsTranscribing);
                case nameof(IsTranscribing):
                case nameof(IsInstallingAsrModel):
                case nameof(CountdownSeconds):
                    OnPropertyChanged(nameof(CanConfigureAudio));
                    break;
                case nameof(IsGeneratingReport):
                    OnPropertyChanged(nameof(CanConfigureAi));
                    OnPropertyChanged(nameof(CanGenerateNotes));
                    OnPropertyChanged(nameof(GenerateNotesLabel));
                    break;
                case nameof(ReportText):
                    OnPropertyChanged(nameof(CanCopyNotes));
                    OnPropertyChanged(nameof(ReportContentVisibility));
                    break;
                case nameof(RecordingTitle):
                    OnPropertyChanged(nameof(WorkspaceTitle));
                    break;
            }
        };
    }

    private void RefreshCurrentReports()
    {
        var selectedId = SelectedReport?.Id;
        CurrentReports.Clear();
        foreach (var report in Reports.Where(report => report.RecordingId == SelectedRecording?.Id))
            CurrentReports.Add(report);
        SelectedReport = CurrentReports.FirstOrDefault(report => report.Id == selectedId);
    }
}
