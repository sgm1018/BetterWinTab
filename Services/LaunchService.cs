using System.Diagnostics;
using System.IO;
using BetterWinTab.Models;

namespace BetterWinTab.Services;

/// <summary>
/// Search-bar launcher: installed apps (Start Menu shortcuts) and folders on disk
/// (via <see cref="FolderIndexService"/>). Used when no window/tab matches the query
/// and in "Apps" search mode.
/// </summary>
public class LaunchService
{
    // Both the per-user and all-users Start Menu Programs folders
    private static readonly string[] StartMenuRoots =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Start Menu\Programs"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            @"Microsoft\Windows\Start Menu\Programs"),
    ];

    // Apps are slightly preferred over folders with the same match quality.
    private const int AppBoost = 300;

    private readonly FolderIndexService _folderIndex;
    private readonly object _cacheLock = new();

    // Cache built lazily (or by WarmUp) — item plus its normalized search key
    private List<(LaunchItem Item, string Key)>? _cache;

    /// <summary>Raised (on a background thread) when new results may be available, e.g. the folder index was updated.</summary>
    public event Action? SourcesChanged;

    public LaunchService(FolderIndexService folderIndex)
    {
        _folderIndex = folderIndex;
        _folderIndex.IndexChanged += () => SourcesChanged?.Invoke();
        WarmUp();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the app cache in the background and refreshes the folder index if it is stale,
    /// so typing in the search bar never waits on disk access.
    /// </summary>
    public void WarmUp()
    {
        _ = Task.Run(EnsureCache);
        _folderIndex.EnsureFresh();
    }

    /// <summary>
    /// Returns up to <paramref name="maxResults"/> apps and folders whose names match the query,
    /// best matches first (exact &gt; prefix &gt; word start &gt; substring).
    /// </summary>
    public IReadOnlyList<LaunchItem> Search(string query, int maxResults = 10)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var q = FolderIndexService.NormalizeKey(query.Trim());

        var apps = EnsureCache()
            .Select(app => (app.Item, Score: FolderIndexService.ScoreName(app.Key, q)))
            .Where(app => app.Score > int.MinValue)
            .Select(app => (app.Item, Score: app.Score + AppBoost));

        var folders = _folderIndex.Search(query, maxResults)
            .Select(folder => (Item: new LaunchItem(folder.Name, folder.Path, LaunchItemKind.Folder), folder.Score));

        return apps.Concat(folders)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .Select(result => result.Item)
            .ToList();
    }

    /// <summary>
    /// Launches a previously found <see cref="LaunchItem"/> via the Windows Shell.
    /// Works for .lnk shortcuts, .exe, folder paths, etc.
    /// </summary>
    public void Launch(LaunchItem item)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = item.ShortcutPath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LaunchService.Launch: {ex.Message}"); }
    }

    /// <summary>
    /// Tries to execute <paramref name="query"/> directly via the Shell,
    /// just like typing into the Run dialog (Win+R). Works for exe names
    /// on PATH, folder paths, file paths, shell: URIs, etc.
    /// Does NOT trigger a browser web-search.
    /// </summary>
    public void RunQuery(string query)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = query.Trim(),
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LaunchService.RunQuery: {ex.Message}"); }
    }

    /// <summary>
    /// Clears the cached shortcut list so it is rebuilt on the next search.
    /// Call this if apps are installed/uninstalled at runtime.
    /// </summary>
    public void InvalidateCache()
    {
        lock (_cacheLock)
            _cache = null;
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private List<(LaunchItem Item, string Key)> EnsureCache()
    {
        lock (_cacheLock)
        {
            if (_cache != null)
                return _cache;

            var items = new List<LaunchItem>();

            foreach (var root in StartMenuRoots)
            {
                if (!Directory.Exists(root))
                    continue;

                try
                {
                    foreach (var lnk in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                    {
                        var name = Path.GetFileNameWithoutExtension(lnk);
                        if (string.IsNullOrWhiteSpace(name))
                            continue;

                        // Skip noise entries typically found in Start Menu
                        if (IsNoiseEntry(name))
                            continue;

                        items.Add(new LaunchItem(name, lnk));
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LaunchService.EnsureCache: {ex.Message}"); }
            }

            // De-duplicate by name (keep first occurrence — user Start Menu wins)
            _cache = items
                .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .Select(i => (i, FolderIndexService.NormalizeKey(i.Name)))
                .ToList();

            return _cache;
        }
    }

    private static readonly string[] _noiseKeywords =
        ["uninstall", "readme", "release notes", "changelog", "help", "documentation", "license"];

    private static bool IsNoiseEntry(string name) =>
        _noiseKeywords.Any(kw => name.Contains(kw, StringComparison.OrdinalIgnoreCase));
}
