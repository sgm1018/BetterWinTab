using System.Diagnostics;
using System.IO;
using BetterWinTab.Models;

namespace BetterWinTab.Services;

/// <summary>
/// Enumerates installed apps from Start Menu shortcuts and provides
/// a no-internet local launcher fallback for the search bar.
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

    // Cache built lazily on first search
    private List<LaunchItem>? _cache;

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns up to <paramref name="maxResults"/> items whose names contain the query.
    /// Exact prefix matches are sorted first.
    /// </summary>
    public IReadOnlyList<LaunchItem> Search(string query, int maxResults = 8)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var all = EnsureCache();
        var q = query.Trim();

        var startsWith = all
            .Where(i => i.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase));

        var contains = all
            .Where(i => !i.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase)
                        && i.Name.Contains(q, StringComparison.OrdinalIgnoreCase));

        return startsWith.Concat(contains).Take(maxResults).ToList();
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
    public void InvalidateCache() => _cache = null;

    // ── Private helpers ───────────────────────────────────────────────────

    private List<LaunchItem> EnsureCache()
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
            .ToList();

        return _cache;
    }

    private static readonly string[] _noiseKeywords =
        ["uninstall", "readme", "release notes", "changelog", "help", "documentation", "license"];

    private static bool IsNoiseEntry(string name) =>
        _noiseKeywords.Any(kw => name.Contains(kw, StringComparison.OrdinalIgnoreCase));
}
