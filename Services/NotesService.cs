using System.IO;
using System.Text.Json;
using BetterWinTab.Models;

namespace BetterWinTab.Services;

/// <summary>
/// Persists notes to %AppData%\BetterWinTab\notes.json.
/// Writes are debounced and atomic (temp file + move) so frequent edits from the editor
/// don't hammer the disk and a crash mid-write can't corrupt the existing file.
/// </summary>
public class NotesService : IDisposable
{
    private static readonly string NotesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BetterWinTab");

    private static readonly string NotesPath = Path.Combine(NotesDir, "notes.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private const int SaveDelayMs = 800;

    private readonly object _sync = new();
    private readonly object _writeLock = new();
    private readonly List<Note> _notes = new();
    private readonly Timer _saveTimer;
    private bool _dirty;

    public NotesService()
    {
        _saveTimer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
        Load();
    }

    /// <summary>Snapshot of the notes in display order.</summary>
    public IReadOnlyList<Note> Notes
    {
        get { lock (_sync) return _notes.ToList(); }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(NotesPath)) return;
            var json = File.ReadAllText(NotesPath);
            var notes = JsonSerializer.Deserialize<List<Note>>(json, JsonOptions);
            if (notes == null) return;
            lock (_sync)
            {
                _notes.Clear();
                _notes.AddRange(notes.Where(n => !string.IsNullOrEmpty(n.Id)).OrderBy(n => n.SortOrder));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NotesService.Load: {ex.Message}");
            // Keep a copy of the unreadable file so the next save doesn't silently destroy it.
            try
            {
                File.Copy(NotesPath, NotesPath + ".corrupt", overwrite: true);
            }
            catch (Exception backupException)
            {
                System.Diagnostics.Debug.WriteLine($"NotesService.Load backup failed: {backupException.Message}");
            }
        }
    }

    public void Insert(int index, Note note)
    {
        lock (_sync)
        {
            index = Math.Clamp(index, 0, _notes.Count);
            _notes.Insert(index, note);
        }
        ScheduleSave();
    }

    public void Remove(string id)
    {
        lock (_sync) _notes.RemoveAll(n => n.Id == id);
        ScheduleSave();
    }

    /// <summary>Applies an edit coming from the editor. Returns false when nothing changed.</summary>
    public bool Update(string id, string title, string html, string font)
    {
        lock (_sync)
        {
            var note = _notes.FirstOrDefault(n => n.Id == id);
            if (note == null) return false;
            if (note.Title == title && note.Html == html && note.Font == font) return false;
            note.Title = title;
            note.Html = html;
            note.Font = font;
            note.UpdatedAt = DateTime.Now;
        }
        ScheduleSave();
        return true;
    }

    public void ScheduleSave()
    {
        lock (_sync) _dirty = true;
        _saveTimer.Change(SaveDelayMs, Timeout.Infinite);
    }

    /// <summary>Writes pending changes to disk immediately.</summary>
    public void Flush()
    {
        lock (_writeLock)
        {
            string json;
            lock (_sync)
            {
                if (!_dirty) return;
                for (int i = 0; i < _notes.Count; i++) _notes[i].SortOrder = i;
                json = JsonSerializer.Serialize(_notes, JsonOptions);
                _dirty = false;
            }

            try
            {
                Directory.CreateDirectory(NotesDir);
                var tmp = NotesPath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, NotesPath, overwrite: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NotesService.Flush: {ex.Message}");
                lock (_sync) _dirty = true;
            }
        }
    }

    public void Dispose()
    {
        _saveTimer.Dispose();
        Flush();
    }
}
