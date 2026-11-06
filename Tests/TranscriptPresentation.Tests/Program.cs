using BetterWinTab.Models;
using BetterWinTab.Models.Transcription;
using BetterWinTab.ViewModels;

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var vm = new CallsViewModel();
vm.ApplyStreamingUpdate(new("partial", "loopback", "turn", "Hola", 2));
var first = vm.TranscriptMessages.Single();
vm.ApplyStreamingUpdate(new("partial", "loopback", "turn", "Hola a todos", 2));
Assert(vm.TranscriptMessages.Count == 1 && ReferenceEquals(first, vm.TranscriptMessages[0]), "Partials must update the same row.");
Assert(first.Text == "Hola a todos" && first.IsProvisional, "The row must contain the latest provisional text.");
vm.ApplyStreamingUpdate(new("partial", "microphone", "you", "Buenos días", 1));
Assert(vm.TranscriptMessages.Count == 2 && vm.TranscriptMessages[0].Speaker == "You", "Independent audio sources each need one provisional row, ordered by time.");
vm.ApplyStreamingUpdate(new("final", "loopback", "turn", Segments:
[
    new("turn:1", "loopback", 2, 1, "Hola", "voice-1", "Speaker 1"),
    new("turn:2", "loopback", 3, 1, "a todos", "voice-2", "Speaker 2")
]));
Assert(vm.TranscriptMessages.Count == 3 && vm.TranscriptMessages.Count(m => m.IsProvisional) == 1, "Final diarization must replace its provisional row without losing either speaker.");
Assert(!vm.TranscriptMessages.Any(m => m.Text == "Hola a todos"), "Finalization must remove the parent partial text.");
var voiceOne = vm.TranscriptMessages.Single(m => m.Speaker == "Speaker 1");
vm.ApplyStreamingUpdate(new("final", "loopback", "return", Segments: [new("return:1", "loopback", 7, 1, "Seguimos", "voice-1", "Speaker 1")]));
Assert(voiceOne.SpeakerBrush.Color == vm.TranscriptMessages.Last().SpeakerBrush.Color, "A returning speaker must keep the same accent.");
Assert(voiceOne.SpeakerBrush.Color != vm.TranscriptMessages.Single(m => m.Speaker == "Speaker 2").SpeakerBrush.Color, "Distinct stable speakers need distinct accents.");
vm.ApplyStreamingUpdate(new("final", "loopback", "return", Segments: [new("return:1", "loopback", 7, 1, "Seguimos", "voice-1", "Speaker 1")]));
Assert(vm.TranscriptMessages.Count == 4, "Repeated finals must not duplicate rows.");
vm.ApplyStreamingUpdate(new("partial", "loopback", "return", "stale", 7));
Assert(!vm.TranscriptMessages.Any(m => m.Text == "stale"), "A delayed partial must not resurrect a confirmed turn.");
vm.ClearTranscriptMessages();
vm.ApplyStreamingUpdate(new("partial", "loopback", "old", "old", 0));
vm.ApplyStreamingUpdate(new("partial", "loopback", "new", "new", 4));
Assert(vm.TranscriptMessages.Count == 1 && vm.TranscriptMessages[0].Text == "new", "Only one provisional row per source may be active.");
vm.ApplyStreamingUpdate(new("final", "loopback", "old", "old", 0, 1));
Assert(vm.TranscriptMessages.Count == 2 && vm.TranscriptMessages.Last().IsProvisional, "An older final must preserve the newer active turn.");
vm.LoadTranscriptMessages([new AudioTranscriptSegment { Text = "Legacy edited text", Speaker = "Edited transcript", StartSeconds = 3661 }, new AudioTranscriptSegment { Id = "saved", Source = "loopback", Text = "Recovered", IsIncomplete = true, IsSpeakerUncertain = true }]);
Assert(vm.TranscriptMessages.Count == 2 && vm.TranscriptMessages.Last().Text == "Legacy edited text", "Legacy recordings must load without parsing or rewriting text.");
Assert(vm.TranscriptMessages.Last().Timestamp == "61:01", "Elapsed timestamps must retain total minutes after one hour.");
Assert(vm.TranscriptMessages[0].Status == "Incomplete · Voice uncertain", "Incomplete and uncertain attribution must remain visible.");
vm.SelectedRecording = new();
Assert(vm.CanTranscribeRecording, "An untouched recording can be transcribed once.");
vm.SelectedRecording.TranscriptionStarted = true;
Assert(!vm.CanTranscribeRecording, "A started transcription must not be offered again.");
await vm.ToggleTranscriptEditingCommand.ExecuteAsync(null);
Assert(vm.IsEditingTranscript, "A stopped recording supports explicit editing.");
vm.IsTranscribing = true;
Assert(!vm.IsEditingTranscript && !vm.CanEditTranscript, "Active transcription must immediately leave editing and guard it.");
await vm.ToggleTranscriptEditingCommand.ExecuteAsync(null);
Assert(!vm.IsEditingTranscript, "The command itself must refuse editing during transcription.");
vm.IsTranscribing = false;
vm.IsRecording = true;
Assert(!vm.CanEditTranscript, "Recording must guard the editor.");
vm.ClearTranscriptMessages();
Assert(vm.TranscriptMessages.Count == 0, "Changing recordings must clear presentation state.");
vm.ApplyStreamingUpdate(new("partial", "loopback", "simultaneous-system", "One", 0));
vm.ApplyStreamingUpdate(new("partial", "microphone", "simultaneous-mic", "Two", 0));
var firstSimultaneous = vm.TranscriptMessages[0];
vm.ApplyStreamingUpdate(new("partial", "loopback", "simultaneous-system", "One updated", 0));
Assert(ReferenceEquals(firstSimultaneous, vm.TranscriptMessages[0]), "Simultaneous sources must not reorder on each partial update.");
Console.WriteLine("Transcript presentation assertions passed.");
