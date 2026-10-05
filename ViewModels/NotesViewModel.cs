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

    [ObservableProperty]
    private NoteItemViewModel? _selectedNote;

    /// <summary>Raised when the note shown in the editor must change.</summary>
    public event Action<NoteItemViewModel?>? SelectedNoteChanged;

    /// <summary>Raised after creating a note so the view can focus the editor title.</summary>
    public event Action? FocusEditorRequested;

    public bool HasNotes => Notes.Count > 0;
    public Visibility EditorVisibility => HasNotes ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyStateVisibility => HasNotes ? Visibility.Collapsed : Visibility.Visible;
    public string NotesCountLabel => Notes.Count == 1 ? "1 note" : $"{Notes.Count} notes";

    public NotesViewModel(NotesService service)
    {
        _service = service;
        foreach (var note in _service.Notes)
            Notes.Add(new NoteItemViewModel(note));

        Notes.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasNotes));
            OnPropertyChanged(nameof(EditorVisibility));
            OnPropertyChanged(nameof(EmptyStateVisibility));
            OnPropertyChanged(nameof(NotesCountLabel));
        };

        _selectedNote = Notes.FirstOrDefault();
    }

    partial void OnSelectedNoteChanged(NoteItemViewModel? value)
    {
        SelectedNoteChanged?.Invoke(value);
    }

    [RelayCommand]
    public void NewNote()
    {
        var note = new Note();
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
        };
        _service.Insert(index + 1, copy);
        var vm = new NoteItemViewModel(copy);
        Notes.Insert(index + 1, vm);
        SelectedNote = vm;
    }

    public void DeleteNote(NoteItemViewModel note)
    {
        var index = Notes.IndexOf(note);
        if (index < 0) return;

        var wasSelected = ReferenceEquals(SelectedNote, note);
        _service.Remove(note.Model.Id);
        Notes.RemoveAt(index);

        if (wasSelected || SelectedNote == null)
        {
            SelectedNote = Notes.Count == 0 ? null : Notes[Math.Min(index, Notes.Count - 1)];
            if (Notes.Count == 0) SelectedNoteChanged?.Invoke(null);
        }
    }

    public void SelectNextNote()
    {
        if (Notes.Count == 0) return;
        var idx = SelectedNote == null ? -1 : Notes.IndexOf(SelectedNote);
        SelectedNote = Notes[(idx + 1) % Notes.Count];
    }

    public void SelectPreviousNote()
    {
        if (Notes.Count == 0) return;
        var idx = SelectedNote == null ? 0 : Notes.IndexOf(SelectedNote);
        SelectedNote = Notes[(idx - 1 + Notes.Count) % Notes.Count];
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

    public void Flush() => _service.Flush();
}
