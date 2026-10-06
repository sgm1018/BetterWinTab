namespace BetterWinTab.ViewModels;

public class NoteFolderItemViewModel(NoteFolder model)
{
    public NoteFolder Model { get; } = model;
    public string Name => Model.Name;
}
