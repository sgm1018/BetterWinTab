using BetterWinTab.AsrWorker;

if (args.Length > 0 && args[0] == "--native-idle") return await NativeIdleTests.Run(args);

var failures = new List<string>();
Check("same voice returns after another speaker", () =>
{
    var speakers = new SessionSpeakers();
    Equal("speaker-1", speakers.Match([1, 0, 0]).Id);
    Equal("speaker-2", speakers.Match([0, 1, 0]).Id);
    Equal("speaker-1", speakers.Match([0.99f, 0.04f, 0]).Id);
});
Check("ambiguous voice is marked uncertain", () =>
{
    var speakers = new SessionSpeakers();
    speakers.Match([1, 0]);
    speakers.Match([0, 1]);
    if (!speakers.Match([0.7f, 0.7f]).Uncertain) throw new Exception("Ambiguous centroid match was accepted");
});
Check("native token times align words to speaker turns", () =>
{
    var segments = TokenAlignment.Align("parent", "loopback", 10, 5, "Hola mundo Otra voz",
        [" Hola", " mun", "do", " Otra", " voz"], [0.2f, 0.8f, 0.9f, 3.2f, 3.8f],
        [new VoiceTurn(0, 2, "speaker-1", "Speaker 1", false), new VoiceTurn(3, 5, "speaker-2", "Speaker 2", false)]);
    Equal(2, segments.Length);
    Equal("Hola mundo", segments[0].Text);
    Equal("Otra voz", segments[1].Text);
    Equal("speaker-2", segments[1].SpeakerId);
    if (Math.Abs(segments[1].StartSeconds - 13.2) > 0.001) throw new Exception("Native timing was lost");
});
Check("overlapping voices remain uncertain without splitting a word", () =>
{
    var segments = TokenAlignment.Align("parent", "loopback", 0, 3, "Hola", [" Ho", "la"], [1f, 1.2f],
        [new VoiceTurn(0, 2, "speaker-1", "Speaker 1", false), new VoiceTurn(1, 3, "speaker-2", "Speaker 2", false)]);
    Equal(1, segments.Length);
    Equal("Hola", segments[0].Text);
    if (!segments[0].IsSpeakerUncertain) throw new Exception("Overlap was assigned as certain");
});
Check("missing token timing preserves text with uncertain speaker", () =>
{
    var segments = TokenAlignment.Align("parent", "loopback", 7, 2, "Texto completo", [], [], []);
    Equal("Texto completo", segments.Single().Text);
    if (!segments[0].IsSpeakerUncertain) throw new Exception("Missing timing was accepted as certain");
});
foreach (var failure in failures) Console.Error.WriteLine(failure);
if (failures.Count > 0) return 1;
Console.WriteLine("PASS: 5 streaming worker assertions");
return 0;

void Check(string name, Action assertion)
{
    try { assertion(); } catch (Exception error) { failures.Add($"FAIL {name}: {error.Message}"); }
}
void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, received {actual}");
}
