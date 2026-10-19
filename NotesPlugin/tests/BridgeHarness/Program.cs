using System.Text.Json;
using BetterWinTab.Services;

var directory = Path.Combine(Path.GetTempPath(), "BetterWinTab-NotesTest-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    using var service = new NotesService(directory);
    var changes = 0;
    var commands = new NotesBridgeCommands(service, (_, _) => changes++);
    JsonElement Call(string action, object arguments) => JsonSerializer.SerializeToElement(commands.Execute(
        JsonSerializer.SerializeToElement(new { action, arguments })));
    void Check(bool pass, string message) { if (!pass) throw new Exception(message); }
    void Reject(Action action)
    {
        try { action(); } catch (ArgumentException) { return; } catch (InvalidOperationException) { return; }
        throw new Exception("Expected request rejection.");
    }
    var folder = service.CreateFolder("Trabajo");
    var created = Call("create_note", new { title = "Prueba ñ", content = "Uno\n<script>alert(1)</script>", folderId = folder.Id });
    var id = created.GetProperty("Id").GetString()!;
    Check(created.GetProperty("Html").GetString()!.Contains("&lt;script&gt;"), "Content must be HTML encoded.");
    Check(File.ReadAllText(Path.Combine(directory, "notes.json")).Contains(id), "Create must persist before returning.");
    var updated = Call("update_note", new { id, expectedUpdatedAt = created.GetProperty("updatedAt").GetString(), appendText = "Más texto" });
    Check(updated.GetProperty("content").GetString()!.EndsWith("Más texto"), "Append missing.");
    Reject(() => Call("update_note", new { id, expectedUpdatedAt = created.GetProperty("updatedAt").GetString(), title = "Stale" }));
    Reject(() => Call("update_note", new { id, expectedUpdatedAt = updated.GetProperty("updatedAt").GetString(), content = "x", appendText = "y" }));
    Reject(() => Call("create_note", new { title = "x", content = "x", folderId = "missing" }));
    Reject(() => Call("create_note", new { title = "x", content = "x", font = "invalid" }));
    Reject(() => Call("get_note", new { id = "missing" }));
    var moved = Call("update_note", new { id, expectedUpdatedAt = updated.GetProperty("updatedAt").GetString(), title = "Nuevo", folderId = (string?)null });
    Check(moved.GetProperty("FolderId").ValueKind == JsonValueKind.Null, "Folder removal failed.");
    Check(moved.GetProperty("Html").GetString() == updated.GetProperty("Html").GetString(), "Title edit changed body.");
    Check(changes == 3, "Rejected edits must not change notes.");
    using (var reloaded = new NotesService(directory))
        Check(reloaded.Notes.Single().Title == "Nuevo", "Persistence reload failed.");
    Console.WriteLine("PASS: persistence, append, encoding, folder move, stale version, validation, unchanged body.");

    if (args.Contains("--serve"))
    {
        var pipeName = "BetterWinTab.Notes.Test." + Guid.NewGuid().ToString("N");
        using var bridge = new NotesBridgeService(request => Task.FromResult(commands.Execute(request)), pipeName);
        Console.WriteLine("READY " + pipeName);
        await Console.In.ReadLineAsync();
    }
}
finally
{
    Directory.Delete(directory, recursive: true);
}
