using BetterWinTab.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace BetterWinTab.ViewModels;

/// <summary>
/// ViewModel wrapper for a WindowFolder displayed in the sidebar.
/// </summary>
public partial class FolderItemViewModel : BaseViewModel
{
    public WindowFolder Model { get; }

    [ObservableProperty]
    private string _icon;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _filterInfo;

    [ObservableProperty]
    private int _windowCount;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private SolidColorBrush _backgroundBrush;

    public FolderItemViewModel(WindowFolder model)
    {
        Model = model;
        _icon = model.Icon;
        _name = model.Name;
        _filterInfo = model.Type switch
        {
            FolderType.All => "All open windows",
            FolderType.SmartProcess => $"Process: {model.ProcessFilter}",
            FolderType.SmartClass => $"Class: {model.ClassNameFilter}",
            FolderType.SmartRules => model.GetFilterSummary(),
            FolderType.Clipboard => "Clipboard history",
            FolderType.Manual => "Custom folder",
            _ => ""
        };
        _windowCount = model.Windows.Count;
        _backgroundBrush = string.IsNullOrEmpty(model.BackgroundColor)
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0))
            : ParseColorBrush(model.BackgroundColor);
    }

    private static SolidColorBrush ParseColorBrush(string hex)
    {
        try
        {
            hex = hex.TrimStart('#');
            byte a = 255;
            byte r, g, b;
            if (hex.Length == 8) { a = Convert.ToByte(hex[..2], 16); r = Convert.ToByte(hex[2..4], 16); g = Convert.ToByte(hex[4..6], 16); b = Convert.ToByte(hex[6..8], 16); }
            else if (hex.Length == 6) { r = Convert.ToByte(hex[..2], 16); g = Convert.ToByte(hex[2..4], 16); b = Convert.ToByte(hex[4..6], 16); }
            else return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 45, 45, 61));
            return new SolidColorBrush(Windows.UI.Color.FromArgb(a, r, g, b));
        }
        catch { return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 45, 45, 61)); }
    }

    /// <summary>
    /// Updates the window count.
    /// </summary>
    public void Refresh()
    {
        WindowCount = Model.Windows.Count;
    }
}
