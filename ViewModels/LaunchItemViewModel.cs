using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using BetterWinTab.Interop;
using BetterWinTab.Models;

namespace BetterWinTab.ViewModels;

/// <summary>
/// Wraps a <see cref="LaunchItem"/> and asynchronously loads the real app icon
/// via Shell32 SHGetFileInfo, so the actual target icon is shown (not the .lnk generic).
/// </summary>
public partial class LaunchItemViewModel : ObservableObject
{
    public LaunchItem Model { get; }

    public string Name => Model.Name;

    /// <summary>
    /// Short friendly path shown under the app name.
    /// Shows "Start Menu" for shortcuts that live in the Start Menu folders, 
    /// otherwise shows just the parent folder name.
    /// </summary>
    public string FriendlyPath { get; }

    [ObservableProperty]
    private BitmapImage? _icon;

    [ObservableProperty]
    private bool _iconLoaded;

    public LaunchItemViewModel(LaunchItem model)
    {
        Model = model;
        FriendlyPath = BuildFriendlyPath(model.ShortcutPath);
        _ = LoadIconAsync();
    }

    // ── Icon loading ───────────────────────────────────────────────

    private async Task LoadIconAsync()
    {
        try
        {
            // Run the GDI/Shell work off the UI thread
            var stream = await Task.Run(() => ExtractIconStream(Model.ShortcutPath));
            if (stream == null) return;

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
            Icon = bitmap;
            IconLoaded = true;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LaunchItemViewModel.LoadIconAsync: {ex.Message}"); }
    }

    /// <summary>
    /// Calls SHGetFileInfo to get the HICON of the shortcut's target, then converts
    /// it to a PNG MemoryStream via System.Drawing.
    /// Must be called off the UI thread.
    /// </summary>
    private static System.IO.MemoryStream? ExtractIconStream(string lnkPath)
    {
        var shfi = new NativeMethods.SHFILEINFO();
        var hr = NativeMethods.SHGetFileInfo(
            lnkPath, 0, ref shfi, (uint)Marshal.SizeOf(shfi),
            NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON);

        if (hr == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
            return null;

        try
        {
            // System.Drawing.Icon wraps the HICON and lets us export as PNG
            using var icon = System.Drawing.Icon.FromHandle(shfi.hIcon);
            using var bmp  = icon.ToBitmap();

            var ms = new System.IO.MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            return ms;
        }
        finally
        {
            NativeMethods.DestroyIcon(shfi.hIcon);
        }
    }

    // ── Friendly path helper ───────────────────────────────────────

    private static readonly string _userStartMenu = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Windows\Start Menu\Programs");

    private static readonly string _allUsersStartMenu = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        @"Microsoft\Windows\Start Menu\Programs");

    private static string BuildFriendlyPath(string path)
    {
        // Strip known start-menu roots to get a short relative label
        string rel = path;
        if (rel.StartsWith(_userStartMenu, StringComparison.OrdinalIgnoreCase))
            rel = rel[_userStartMenu.Length..].TrimStart('\\', '/');
        else if (rel.StartsWith(_allUsersStartMenu, StringComparison.OrdinalIgnoreCase))
            rel = rel[_allUsersStartMenu.Length..].TrimStart('\\', '/');

        // If it's directly in Programs root, just say "Start Menu"
        var dir = Path.GetDirectoryName(rel);
        return string.IsNullOrEmpty(dir) ? "Start Menu" : dir.Replace('\\', ' ').Replace('/', ' ').Trim();
    }
}

