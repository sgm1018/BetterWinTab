using System.Collections.Concurrent;
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
    public bool IsFolder => Model.Kind == LaunchItemKind.Folder;
    public string ItemTypeLabel => IsFolder ? "FOLDER" : "APP";
    public string FallbackGlyph => IsFolder ? "\uE8B7" : "\uE8A5";

    /// <summary>
    /// Short friendly path shown under the item name.
    /// Apps: "Start Menu" or the Start Menu sub-folder. Folders: the parent folder's full path.
    /// </summary>
    public string FriendlyPath { get; }

    [ObservableProperty]
    private BitmapImage? _icon;

    [ObservableProperty]
    private bool _iconLoaded;

    // Icons are cached as PNG bytes so re-typing a query doesn't hit the Shell again.
    private static readonly ConcurrentDictionary<string, byte[]> IconCache = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxCachedIcons = 512;

    public LaunchItemViewModel(LaunchItem model)
    {
        Model = model;
        FriendlyPath = IsFolder
            ? Path.GetDirectoryName(model.ShortcutPath.TrimEnd('\\')) ?? model.ShortcutPath
            : BuildFriendlyPath(model.ShortcutPath);
        _ = LoadIconAsync();
    }

    // ── Icon loading ───────────────────────────────────────────────

    private async Task LoadIconAsync()
    {
        try
        {
            if (!IconCache.TryGetValue(Model.ShortcutPath, out var png))
            {
                // Run the GDI/Shell work off the UI thread
                png = await Task.Run(() => ExtractIconPng(Model.ShortcutPath));
                if (png == null) return;
                if (IconCache.Count >= MaxCachedIcons)
                    IconCache.Clear();
                IconCache[Model.ShortcutPath] = png;
            }

            using var stream = new System.IO.MemoryStream(png);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
            Icon = bitmap;
            IconLoaded = true;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LaunchItemViewModel.LoadIconAsync: {ex.Message}"); }
    }

    /// <summary>
    /// Calls SHGetFileInfo to get the HICON of the shortcut's target (or folder), then converts
    /// it to PNG bytes via System.Drawing.
    /// Must be called off the UI thread.
    /// </summary>
    private static byte[]? ExtractIconPng(string lnkPath)
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

            using var ms = new System.IO.MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
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

