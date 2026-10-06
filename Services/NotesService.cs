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
    private static readonly string FoldersPath = Path.Combine(NotesDir, "notes-folders.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private const int SaveDelayMs = 800;

    private readonly object _sync = new();
    private readonly object _writeLock = new();
    private readonly List<Note> _notes = new();
    private readonly List<NoteFolder> _folders = new();
    private readonly Timer _saveTimer;
    private bool _dirty;

    public NotesService()
    {
        _saveTimer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
        LoadFolders();
        Load();
    }

    /// <summary>Snapshot of the notes in display order.</summary>
    public IReadOnlyList<Note> Notes
    {
        get { lock (_sync) return _notes.ToList(); }
    }

    public IReadOnlyList<NoteFolder> Folders
    {
        get { lock (_sync) return _folders.ToList(); }
    }

    private void LoadFolders()
    {
        try
        {
            if (!File.Exists(FoldersPath)) return;
            var json = File.ReadAllText(FoldersPath);
            var folders = JsonSerializer.Deserialize<List<NoteFolder>>(json, JsonOptions);
            if (folders == null) return;
            lock (_sync)
            {
                _folders.Clear();
                _folders.AddRange(folders
                    .Where(folder => !string.IsNullOrWhiteSpace(folder.Id) && !string.IsNullOrWhiteSpace(folder.Name))
                    .OrderBy(folder => folder.SortOrder));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NotesService.LoadFolders: {ex.Message}");
            try
            {
                File.Copy(FoldersPath, FoldersPath + ".corrupt", overwrite: true);
            }
            catch (Exception backupException)
            {
                System.Diagnostics.Debug.WriteLine($"NotesService.LoadFolders backup failed: {backupException.Message}");
            }
        }
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
                var folderIds = _folders.Select(folder => folder.Id).ToHashSet(StringComparer.Ordinal);
                foreach (var note in _notes)
                {
                    if (note.FolderId != null && !folderIds.Contains(note.FolderId))
                        note.FolderId = null;
                }
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

    public NoteFolder CreateFolder(string name)
    {
        var folder = new NoteFolder { Name = name };
        lock (_sync)
        {
            folder.SortOrder = _folders.Count;
            _folders.Add(folder);
        }
        ScheduleSave();
        return folder;
    }

    public bool DeleteFolder(string id)
    {
        lock (_sync)
        {
            if (_folders.RemoveAll(folder => folder.Id == id) == 0)
                return false;
            foreach (var note in _notes.Where(note => note.FolderId == id))
                note.FolderId = null;
        }
        ScheduleSave();
        return true;
    }

    public bool MoveToFolder(string id, string? folderId)
    {
        lock (_sync)
        {
            var note = _notes.FirstOrDefault(item => item.Id == id);
            if (note == null || (folderId != null && !_folders.Any(folder => folder.Id == folderId)))
                return false;
            if (note.FolderId == folderId) return false;
            note.FolderId = folderId;
        }
        ScheduleSave();
        return true;
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
            string notesJson;
            string foldersJson;
            lock (_sync)
            {
                if (!_dirty) return;
                for (int i = 0; i < _notes.Count; i++) _notes[i].SortOrder = i;
                for (int i = 0; i < _folders.Count; i++) _folders[i].SortOrder = i;
                notesJson = JsonSerializer.Serialize(_notes, JsonOptions);
                foldersJson = JsonSerializer.Serialize(_folders, JsonOptions);
                _dirty = false;
            }

            try
            {
                Directory.CreateDirectory(NotesDir);
                WriteAtomically(NotesPath, notesJson);
                WriteAtomically(FoldersPath, foldersJson);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NotesService.Flush: {ex.Message}");
                lock (_sync) _dirty = true;
            }
        }
    }

    private static void WriteAtomically(string path, string contents)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, contents);
        File.Move(tmp, path, overwrite: true);
    }

    public void Dispose()
    {
        _saveTimer.Dispose();
        Flush();
    }
}
