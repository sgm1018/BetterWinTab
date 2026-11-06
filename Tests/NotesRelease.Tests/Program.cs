using System.Text.Json;
using BetterWinTab.Models;
using BetterWinTab.Services;

var directory = Path.Combine(Path.GetTempPath(), "BetterWinTab-NotesRelease-" + Guid.NewGuid().ToString("N"));
try
{
    Console.WriteLine($"JSON reflection enabled: {JsonSerializer.IsReflectionEnabledByDefault}");
    using var service = new NotesService(directory);
    var note = new Note { Title = "Release note ñ", Html = "<p>Saved content</p>" };
    service.Insert(0, note);
    service.FlushOrThrow();

    using var reloaded = new NotesService(directory);
    var saved = reloaded.Notes.Single();
    if (saved.Id != note.Id || saved.Title != note.Title || saved.Html != note.Html)
        throw new Exception("A note must survive save and reload in the published app.");

    var payload = JsonSerializer.Serialize(new { type = "load", id = note.Id, title = note.Title, html = note.Html, font = note.Font });
    using var message = JsonDocument.Parse(payload);
    if (message.RootElement.GetProperty("id").GetString() != note.Id)
        throw new Exception("The notes editor message must preserve the note ID.");
    if (!JsonSerializer.IsReflectionEnabledByDefault)
        throw new Exception("Published notes require reflection-based JSON serialization.");

    Console.WriteLine("PASS: published note save/reload and editor JSON serialization.");
}
finally
{
    if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
}
