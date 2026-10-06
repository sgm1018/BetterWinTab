using System.Collections.ObjectModel;

namespace BetterWinTab.ViewModels;

/// <summary>
/// Drives the Notes panel: note list (sidebar), selection and create/clone/delete.
/// Content editing happens in the WebView2-hosted editor; edits flow back via <see cref="ApplyEditorChange"/>.
/// </summary>
public partial class NotesViewModel : BaseViewModel
{
    private readonly NotesService _service;

    public ObservableCollection<NoteItemViewModel> Notes { get; } = new();
    public ObservableCollection<NoteItemViewModel> VisibleNotes { get; } = new();
    public ObservableCollection<NoteFolderItemViewModel> Folders { get; } = new();

    [ObservableProperty]
    private NoteItemViewModel? _selectedNote;

    [ObservableProperty]
    private NoteFolderItemViewModel? _selectedFolder;

    /// <summary>Raised when the note shown in the editor must change.</summary>
    public event Action<NoteItemViewModel?>? SelectedNoteChanged;

    /// <summary>Raised after creating a note so the view can focus the editor title.</summary>
    public event Action? FocusEditorRequested;

    public bool HasNotes => VisibleNotes.Count > 0;
    public Visibility EditorVisibility => HasNotes ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyStateVisibility => HasNotes ? Visibility.Collapsed : Visibility.Visible;
    public string EmptyStateTitle => SelectedFolder == null ? "No notes yet" : "This folder is empty";
    public string NotesCountLabel => VisibleNotes.Count == 1 ? "1 note" : $"{VisibleNotes.Count} notes";
    public bool IsAllNotesSelected => SelectedFolder == null;

    public NotesViewModel(NotesService service)
    {
        _service = service;
        foreach (var note in _service.Notes)
            Notes.Add(new NoteItemViewModel(note));
        foreach (var folder in _service.Folders)
            Folders.Add(new NoteFolderItemViewModel(folder));

        Notes.CollectionChanged += (_, _) => RefreshVisibleNotes();
        VisibleNotes.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasNotes));
            OnPropertyChanged(nameof(EditorVisibility));
            OnPropertyChanged(nameof(EmptyStateVisibility));
            OnPropertyChanged(nameof(NotesCountLabel));
        };

        RefreshVisibleNotes();
        _selectedNote = VisibleNotes.FirstOrDefault();
    }

    partial void OnSelectedFolderChanged(NoteFolderItemViewModel? value)
    {
        OnPropertyChanged(nameof(IsAllNotesSelected));
        OnPropertyChanged(nameof(EmptyStateTitle));
        RefreshVisibleNotes();
    }

    partial void OnSelectedNoteChanged(NoteItemViewModel? value)
    {
        SelectedNoteChanged?.Invoke(value);
    }

    [RelayCommand]
    public void NewNote()
    {
        var note = new Note { FolderId = SelectedFolder?.Model.Id };
        _service.Insert(0, note);
        var vm = new NoteItemViewModel(note);
        Notes.Insert(0, vm);
        SelectedNote = vm;
        FocusEditorRequested?.Invoke();
    }

    public void CloneNote(NoteItemViewModel source)
    {
        var index = Notes.IndexOf(source);
        if (index < 0) return;

        var copy = new Note
        {
            Title = string.IsNullOrWhiteSpace(source.Model.Title) ? "Untitled (copy)" : $"{source.Model.Title} (copy)",
            Html = source.Model.Html,
            Font = source.Model.Font,
            FolderId = source.Model.FolderId,
        };
        _service.Insert(index + 1, copy);
        var vm = new NoteItemViewModel(copy);
        Notes.Insert(index + 1, vm);
        SelectedNote = vm;
    }

    public void DeleteNote(NoteItemViewModel note)
    {
        var index = VisibleNotes.IndexOf(note);
        var notesIndex = Notes.IndexOf(note);
        if (index < 0 || notesIndex < 0) return;

        var wasSelected = ReferenceEquals(SelectedNote, note);
        _service.Remove(note.Model.Id);
        Notes.RemoveAt(notesIndex);

        if (wasSelected || SelectedNote == null)
        {
            SelectedNote = VisibleNotes.Count == 0 ? null : VisibleNotes[Math.Min(index, VisibleNotes.Count - 1)];
            if (VisibleNotes.Count == 0) SelectedNoteChanged?.Invoke(null);
        }
    }

    public void CreateFolder(string name)
    {
        var folder = _service.CreateFolder(name);
        var folderVm = new NoteFolderItemViewModel(folder);
        Folders.Add(folderVm);
        SelectedFolder = folderVm;
    }

    public void DeleteFolder(NoteFolderItemViewModel folder)
    {
        if (!Folders.Contains(folder) || !_service.DeleteFolder(folder.Model.Id)) return;
        if (ReferenceEquals(SelectedFolder, folder))
            SelectedFolder = null;
        Folders.Remove(folder);
    }

    public void SelectAllNotes() => SelectedFolder = null;

    public void MoveNoteToFolder(NoteItemViewModel note, NoteFolderItemViewModel? folder)
    {
        if (!Notes.Contains(note) || !_service.MoveToFolder(note.Model.Id, folder?.Model.Id)) return;
        note.Model.FolderId = folder?.Model.Id;
        RefreshVisibleNotes();
    }

    public void SelectNextNote()
    {
        if (VisibleNotes.Count == 0) return;
        var idx = SelectedNote == null ? -1 : VisibleNotes.IndexOf(SelectedNote);
        SelectedNote = VisibleNotes[(idx + 1) % VisibleNotes.Count];
    }

    public void SelectPreviousNote()
    {
        if (VisibleNotes.Count == 0) return;
        var idx = SelectedNote == null ? 0 : VisibleNotes.IndexOf(SelectedNote);
        SelectedNote = VisibleNotes[(idx - 1 + VisibleNotes.Count) % VisibleNotes.Count];
    }

    /// <summary>Applies content sent by the editor (title, HTML, font, plain-text preview).</summary>
    public void ApplyEditorChange(string id, string title, string html, string font, string? preview)
    {
        var vm = Notes.FirstOrDefault(n => n.Model.Id == id);
        if (vm == null) return;
        if (_service.Update(id, title.Trim(), html, string.IsNullOrEmpty(font) ? "handwritten" : font))
            vm.ApplyModelChanges(preview);
    }

    public void RefreshTimestamps()
    {
        foreach (var note in Notes) note.RefreshUpdatedLabel();
    }

    private void RefreshVisibleNotes()
    {
        var previousSelection = SelectedNote;
        var folderId = SelectedFolder?.Model.Id;
        var visible = Notes.Where(note => note.Model.FolderId == folderId).ToList();
        VisibleNotes.Clear();
        foreach (var note in visible)
            VisibleNotes.Add(note);

        if (previousSelection != null && VisibleNotes.Contains(previousSelection))
            SelectedNote = previousSelection;
        else
            SelectedNote = VisibleNotes.FirstOrDefault();
    }

    public void Flush() => _service.Flush();
}
