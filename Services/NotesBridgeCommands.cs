using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using BetterWinTab.Models;

namespace BetterWinTab.Services;

/// <summary>Runs on the UI thread after pending editor changes have been received.</summary>
public sealed class NotesBridgeCommands
{
    private readonly NotesService _service;
    private readonly Action<Note, bool> _changed;

    public NotesBridgeCommands(NotesService service, Action<Note, bool> changed)
    {
        _service = service;
        _changed = changed;
    }

    public object Execute(JsonElement request)
    {
        if (request.ValueKind != JsonValueKind.Object) throw new ArgumentException("Expected an object.");
        var action = Required(request, "action", 64);
        var args = request.TryGetProperty("arguments", out var arguments) ? arguments : JsonSerializer.SerializeToElement(new { });
        if (args.ValueKind != JsonValueKind.Object) throw new ArgumentException("Expected arguments object.");
        switch (action)
        {
            case "status":
                return new { connected = true, protocolVersion = 1, notes = _service.Notes.Count };
            case "list_folders":
                return new { folders = _service.Folders };
            case "list_notes":
            {
                var query = Optional(args, "query", 500) ?? "";
                var offset = Number(args, "offset", 0, 0, int.MaxValue);
                var limit = Number(args, "limit", 50, 1, 100);
                var notes = _service.Notes.Where(n => n.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || PlainText(n.Html).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
                return new { total = notes.Count, offset, notes = notes.Skip(offset).Take(limit).Select(n => new
                {
                    n.Id, n.Title, n.FolderId, updatedAt = n.UpdatedAt.ToString("O"),
                    preview = PlainText(n.Html) is var text && text.Length > 200 ? text[..200] : PlainText(n.Html)
                }).ToList() };
            }
            case "get_note":
                return Describe(Find(args));
            case "create_note":
            {
                var title = Required(args, "title", 500).Trim();
                var content = Required(args, "content", 200_000);
                var folderId = Folder(args, null);
                var font = Font(args, "handwritten");
                var note = new Note { Title = title, Html = ToHtml(content), FolderId = folderId, Font = font };
                _service.Insert(0, note);
                _changed(note, true);
                _service.FlushOrThrow();
                return Describe(note);
            }
            case "update_note":
            {
                var note = Find(args);
                var expected = Required(args, "expectedUpdatedAt", 64);
                if (expected != note.UpdatedAt.ToString("O"))
                    throw new InvalidOperationException("La nota ha cambiado. Vuelve a leerla con get_note antes de modificarla.");
                var title = Optional(args, "title", 500)?.Trim() ?? note.Title;
                var content = Optional(args, "content", 200_000);
                var append = Optional(args, "appendText", 200_000);
                if (content != null && append != null) throw new ArgumentException("Use content or appendText, not both.");
                var folderId = Folder(args, note.FolderId);
                var font = Font(args, note.Font);
                if (!new[] { "title", "content", "appendText", "folderId", "font" }.Any(p => args.TryGetProperty(p, out _)))
                    throw new ArgumentException("Provide at least one field to update.");
                var html = content != null ? ToHtml(content) : note.Html + (append != null ? ToHtml(append) : "");
                _service.Update(note.Id, title, html, font);
                if (folderId != note.FolderId)
                {
                    _service.MoveToFolder(note.Id, folderId);
                    note.UpdatedAt = DateTime.Now;
                }
                _changed(note, false);
                _service.FlushOrThrow();
                return Describe(note);
            }
            default: throw new ArgumentException("Unknown notes action.");
        }
    }

    private Note Find(JsonElement args)
    {
        var id = Required(args, "id", 64);
        return _service.Notes.FirstOrDefault(n => n.Id == id) ?? throw new ArgumentException("Note not found.");
    }

    private string? Folder(JsonElement args, string? fallback)
    {
        if (!args.TryGetProperty("folderId", out var folder)) return fallback;
        if (folder.ValueKind == JsonValueKind.Null) return null;
        var id = Required(args, "folderId", 64);
        if (!_service.Folders.Any(f => f.Id == id)) throw new ArgumentException("Folder not found. Use list_folders.");
        return id;
    }

    private static string Font(JsonElement args, string fallback)
    {
        var font = Optional(args, "font", 32) ?? fallback;
        if (font is not ("handwritten" or "sans" or "serif" or "mono")) throw new ArgumentException("Invalid font.");
        return font;
    }

    private static object Describe(Note note) => new
    {
        note.Id, note.Title, content = PlainText(note.Html), note.Html, note.Font, note.FolderId,
        createdAt = note.CreatedAt.ToString("O"), updatedAt = note.UpdatedAt.ToString("O")
    };

    private static string ToHtml(string content) => string.Join("", content.Replace("\r\n", "\n").Replace('\r', '\n')
        .Split('\n').Select(line => $"<p>{(line.Length == 0 ? "<br>" : WebUtility.HtmlEncode(line))}</p>"));

    private static string PlainText(string html) => WebUtility.HtmlDecode(Regex.Replace(
        Regex.Replace(html, @"<(br\b[^>]*|/(p|div|h[1-6]|li|blockquote|pre))>", "\n", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)),
        "<[^>]+>", "", RegexOptions.None, TimeSpan.FromSeconds(1))).TrimEnd('\n');

    private static string Required(JsonElement args, string name, int max) =>
        Optional(args, name, max) ?? throw new ArgumentException($"Missing {name}.");

    private static string? Optional(JsonElement args, string name, int max)
    {
        if (!args.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind != JsonValueKind.String) throw new ArgumentException($"{name} must be a string.");
        var text = value.GetString()!;
        if (text.Length > max) throw new ArgumentException($"{name} is too long (maximum {max}).");
        return text;
    }

    private static int Number(JsonElement args, string name, int fallback, int min, int max)
    {
        if (!args.TryGetProperty(name, out var value)) return fallback;
        if (!value.TryGetInt32(out var number) || number < min || number > max) throw new ArgumentException($"Invalid {name}.");
        return number;
    }
}
