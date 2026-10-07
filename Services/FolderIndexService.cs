using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.IO.Enumeration;
using System.Runtime.InteropServices;
using System.Text;
using BetterWinTab.Interop;

namespace BetterWinTab.Services;

public readonly record struct FolderMatch(string Name, string Path, int Score);

/// <summary>
/// In-memory index of the folders on the local fixed drives, so the search bar can suggest
/// folders instantly (no disk access per keystroke).
///
/// • The index is persisted to %AppData%\BetterWinTab\folder-index.txt and loaded on startup,
///   so results are available right away even before the first crawl finishes.
/// • A low-priority background thread re-crawls the drives when the index is stale.
/// • FileSystemWatchers pick up folders created/renamed in between crawls.
/// • Known folders (Desktop, Downloads, …) are also indexed by their localized display name
///   ("Descargas", "Documentos", …).
/// </summary>
public sealed class FolderIndexService : IDisposable
{
    private const int MaxEntries = 500_000;
    private const int MaxDepth = 16;
    private const int PublishEvery = 5_000;
    private const int MaxRecentFolders = 5_000;
    private const int KnownFolderBoost = 250;
    private const string CacheHeader = "BWT-FOLDER-INDEX v1";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BetterWinTab");
    private static readonly string CachePath = Path.Combine(CacheDir, "folder-index.txt");

    private static readonly string UserProfile =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Noise folders skipped at any depth (build output, package caches, VCS metadata…).</summary>
    private static readonly HashSet<string> SkippedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", ".svn", ".hg", ".vs", ".idea", "__pycache__", ".pytest_cache",
        ".mypy_cache", ".tox", ".venv", "site-packages", ".gradle", ".m2", ".nuget", ".npm",
        ".cache", ".cargo", ".rustup", "obj", "$Recycle.Bin", "System Volume Information",
        "Config.Msi", "$WinREAgent", "$SysReset", "$Windows.~BT", "$Windows.~WS", "MSOCache",
    };

    /// <summary>Drive-root folders that are listed but not crawled (huge and rarely browsed).</summary>
    private static readonly HashSet<string> DriveRootNoRecurse = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows", "Windows.old", "ProgramData", "PerfLogs", "Recovery", "Users",
    };

    /// <summary>Drive-root folders that are only crawled a couple of levels deep (vendor\product).</summary>
    private static readonly HashSet<string> DriveRootShallow = new(StringComparer.OrdinalIgnoreCase)
    {
        "Program Files", "Program Files (x86)",
    };

    private static readonly (Guid Id, string Fallback)[] KnownFolderIds =
    [
        (new Guid("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"), "Desktop"),
        (new Guid("FDD39AD0-238F-46AF-ADB4-6C85480369C7"), "Documents"),
        (new Guid("374DE290-123F-4565-9164-39C4925E467B"), "Downloads"),
        (new Guid("33E28130-4E1E-4676-835A-98395C3BC3BB"), "Pictures"),
        (new Guid("4BD8D571-6D19-48D3-BE97-422220080E43"), "Music"),
        (new Guid("18989B1D-99B5-455B-841C-AB7C74E4DDFC"), "Videos"),
        (new Guid("5E6C858F-0E22-4760-9AFE-EA3317B67173"), "Profile"),
    ];

    /// <summary>
    /// A folder in the index. Paths are not stored per entry (it would cost tens of MB);
    /// they are rebuilt on demand by walking <see cref="Parent"/> links. Root entries
    /// (<see cref="Parent"/> == -1) store their full path in <see cref="Name"/>.
    /// </summary>
    private readonly record struct Entry(string Name, string Key, int Parent, int Depth);

    private sealed record KnownFolder(string DisplayName, string[] Keys, string Path);

    private sealed record RecentFolder(string Name, string Key, string Path, long AddedTicks);

    private volatile Entry[] _entries = [];
    private volatile KnownFolder[] _knownFolders = [];
    private readonly ConcurrentDictionary<string, RecentFolder> _recentFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly CancellationTokenSource _disposeCts = new();
    private int _building;
    private long _lastBuildUtcTicks;
    private volatile bool _initialized;

    /// <summary>Raised (on a background thread) whenever new search results may be available.</summary>
    public event Action? IndexChanged;

    public FolderIndexService()
    {
        StartBackgroundWork();
    }

    /// <summary>Starts a background re-crawl if the index is older than the refresh interval.</summary>
    public void EnsureFresh() => StartBackgroundWork();

    // ── Search ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the best folder matches for <paramref name="query"/>. Runs entirely in memory
    /// (except for typed paths such as "C:\Users\", which are listed directly from disk).
    /// </summary>
    public IReadOnlyList<FolderMatch> Search(string query, int maxResults)
    {
        var raw = query.Trim().Trim('"');
        if (raw.Length == 0 || maxResults <= 0)
            return [];

        if (LooksLikePath(raw))
            return SearchPath(raw, maxResults);

        var q = NormalizeKey(raw);
        var tokens = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return [];

        var top = new TopResults(maxResults * 3);
        var entries = _entries;

        for (int i = 0; i < entries.Length; i++)
        {
            var score = ScoreEntry(entries, i, q, tokens);
            if (score > int.MinValue)
                top.Add(score - entries[i].Depth * 8, i);
        }

        var results = new List<FolderMatch>(maxResults * 4);
        foreach (var (score, index) in top.Items)
            results.Add(new FolderMatch(GetDisplayName(entries, index), BuildPath(entries, index), score));

        foreach (var known in _knownFolders)
        {
            int best = int.MinValue;
            foreach (var key in known.Keys)
                best = Math.Max(best, ScoreName(key, q));
            if (best > int.MinValue)
                results.Add(new FolderMatch(known.DisplayName, known.Path, best + KnownFolderBoost));
        }

        foreach (var recent in _recentFolders.Values)
        {
            var score = ScoreName(recent.Key, q);
            if (score > int.MinValue)
                results.Add(new FolderMatch(recent.Name, recent.Path, score - CountSeparators(recent.Path) * 8));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var final = new List<FolderMatch>(maxResults);
        foreach (var match in results.OrderByDescending(r => r.Score))
        {
            if (!seen.Add(match.Path.TrimEnd('\\')))
                continue;
            // Cheap metadata check so folders deleted since the last crawl never show up.
            if (!Directory.Exists(match.Path))
                continue;
            final.Add(match);
            if (final.Count >= maxResults)
                break;
        }
        return final;
    }

    private static int ScoreEntry(Entry[] entries, int index, string query, string[] tokens)
    {
        var key = entries[index].Key;
        var score = ScoreName(key, query);
        if (score > int.MinValue || tokens.Length < 2)
            return score;

        // Multi-word query ("proyectos betterwintab"): the last word must match the folder
        // name and every other word must match one of its parent folders.
        score = ScoreName(key, tokens[^1]);
        if (score == int.MinValue)
            return score;

        for (int t = 0; t < tokens.Length - 1; t++)
        {
            bool found = false;
            for (int p = entries[index].Parent; p >= 0 && !found; p = entries[p].Parent)
                found = entries[p].Key.Contains(tokens[t], StringComparison.Ordinal);
            if (!found)
                return int.MinValue;
        }
        return score - 150;
    }

    /// <summary>
    /// Scores a normalized name against a normalized query: exact &gt; prefix &gt; word start &gt; substring.
    /// Returns <see cref="int.MinValue"/> when it does not match.
    /// </summary>
    internal static int ScoreName(string key, string query)
    {
        int index = key.IndexOf(query, StringComparison.Ordinal);
        if (index < 0)
            return int.MinValue;

        int score = index == 0
            ? (key.Length == query.Length ? 1000 : 800)
            : IsWordStart(key, index) ? 600 : 400;
        return score - Math.Min(100, (key.Length - query.Length) * 2);
    }

    private static bool IsWordStart(string key, int index)
        => index == 0 || " -_.()[]\\".Contains(key[index - 1]);

    /// <summary>Lower-cases and strips diacritics so "musica" finds "Música".</summary>
    internal static string NormalizeKey(string value)
    {
        var lower = value.ToLowerInvariant();
        bool ascii = true;
        foreach (var c in lower)
        {
            if (c > 127) { ascii = false; break; }
        }
        if (ascii)
            return lower;

        var decomposed = lower.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static bool LooksLikePath(string query)
        => query.Contains('\\') || query.Contains('/') || query.Contains(':')
           || query.StartsWith('%') || query.StartsWith('~');

    /// <summary>Path-style queries ("C:\Us", "%appdata%\", "~\Doc") list matching subfolders directly.</summary>
    private static IReadOnlyList<FolderMatch> SearchPath(string raw, int maxResults)
    {
        var results = new List<FolderMatch>();
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(raw).Replace('/', '\\');
            if (expanded.StartsWith('~'))
                expanded = UserProfile + expanded[1..];

            // Avoid blocking the UI on network shares.
            if (expanded.StartsWith(@"\\", StringComparison.Ordinal) || !Path.IsPathFullyQualified(expanded))
                return results;

            string? parent;
            string prefix;
            if (expanded.EndsWith('\\'))
            {
                parent = expanded;
                prefix = string.Empty;
            }
            else
            {
                if (Directory.Exists(expanded))
                    results.Add(new FolderMatch(GetDirectoryDisplayName(expanded), expanded, 2000));
                parent = Path.GetDirectoryName(expanded);
                prefix = Path.GetFileName(expanded);
            }

            if (parent == null || !Directory.Exists(parent))
                return results;

            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                AttributesToSkip = 0,
            };
            var children = new FileSystemEnumerable<string>(parent,
                (ref FileSystemEntry entry) => entry.ToFullPath(), options)
            {
                ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                    entry.IsDirectory && !IsHiddenSystem(entry.Attributes)
            };

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (prefix.Length > 0 && !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.Equals(child, expanded, StringComparison.OrdinalIgnoreCase))
                    continue;
                results.Add(new FolderMatch(name, child, 1500 - name.Length));
                if (results.Count >= maxResults * 4)
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FolderIndexService.SearchPath: {ex.Message}");
        }

        return results
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToList();
    }

    private static string GetDirectoryDisplayName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\'));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private static string GetDisplayName(Entry[] entries, int index)
        => entries[index].Parent < 0 ? GetDirectoryDisplayName(entries[index].Name) : entries[index].Name;

    private static string BuildPath(Entry[] entries, int index)
    {
        var parts = new Stack<string>();
        int current = index;
        while (entries[current].Parent >= 0)
        {
            parts.Push(entries[current].Name);
            current = entries[current].Parent;
        }

        var builder = new StringBuilder(entries[current].Name.TrimEnd('\\'));
        foreach (var part in parts)
            builder.Append('\\').Append(part);
        return builder.Length == 2 && builder[1] == ':' ? builder.Append('\\').ToString() : builder.ToString();
    }

    private static int CountSeparators(string path)
    {
        int count = 0;
        foreach (var c in path)
            if (c == '\\') count++;
        return count;
    }

    private static bool IsHiddenSystem(FileAttributes attributes)
        => (attributes & (FileAttributes.Hidden | FileAttributes.System)) == (FileAttributes.Hidden | FileAttributes.System);

    /// <summary>Keeps the N highest scores without sorting the whole index.</summary>
    private sealed class TopResults(int capacity)
    {
        private readonly List<(int Score, int Index)> _items = new(capacity + 1);
        public IReadOnlyList<(int Score, int Index)> Items => _items;

        public void Add(int score, int index)
        {
            if (_items.Count >= capacity && score <= _items[^1].Score)
                return;

            int position = _items.Count;
            while (position > 0 && _items[position - 1].Score < score)
                position--;
            _items.Insert(position, (score, index));
            if (_items.Count > capacity)
                _items.RemoveAt(_items.Count - 1);
        }
    }

    // ── Background work ─────────────────────────────────────────────────

    private void StartBackgroundWork()
    {
        if (_disposeCts.IsCancellationRequested)
            return;
        if (_initialized && DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastBuildUtcTicks) < RefreshInterval.Ticks)
            return;
        if (Interlocked.CompareExchange(ref _building, 1, 0) != 0)
            return;

        var thread = new Thread(BackgroundWork)
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "BetterWinTab folder index",
        };
        thread.Start();
    }

    private void BackgroundWork()
    {
        try
        {
            var token = _disposeCts.Token;
            if (!_initialized)
            {
                _initialized = true;
                LoadKnownFolders();
                LoadCache();
                StartWatchers();
                RaiseIndexChanged();
            }

            if (DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastBuildUtcTicks) >= RefreshInterval.Ticks)
                Build(token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FolderIndexService: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _building, 0);
        }
    }

    private void Build(CancellationToken token)
    {
        var buildStartTicks = DateTime.UtcNow.Ticks;
        bool publishIncrementally = _entries.Length == 0;
        var entries = new List<Entry>(Math.Max(_entries.Length + 1024, 65_536));
        var queue = new Queue<(int Index, string Path, int RemainingDepth)>();
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false,
        };

        foreach (var root in GetCrawlRoots())
        {
            entries.Add(new Entry(root, NormalizeKey(GetDirectoryDisplayName(root)), -1, 0));
            queue.Enqueue((entries.Count - 1, root, MaxDepth));
        }

        int lastPublished = 0;
        while (queue.Count > 0 && entries.Count < MaxEntries)
        {
            token.ThrowIfCancellationRequested();
            var (parentIndex, parentPath, remainingDepth) = queue.Dequeue();
            bool parentIsDriveRoot = entries[parentIndex].Parent < 0 && IsDriveRoot(parentPath);
            bool parentIsProfile = string.Equals(parentPath, UserProfile, StringComparison.OrdinalIgnoreCase);

            try
            {
                var children = new FileSystemEnumerable<(string Name, FileAttributes Attributes)>(
                    parentPath,
                    (ref FileSystemEntry entry) => (entry.FileName.ToString(), entry.Attributes),
                    options)
                {
                    ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                        entry.IsDirectory && !IsHiddenSystem(entry.Attributes)
                };

                foreach (var (name, attributes) in children)
                {
                    if (SkippedNames.Contains(name))
                        continue;

                    var key = NormalizeKey(name);
                    entries.Add(new Entry(name, key == name ? name : key, parentIndex,
                        ChildDepth(entries[parentIndex], parentIsProfile, name)));
                    var childIndex = entries.Count - 1;

                    int childDepth = remainingDepth - 1;
                    if (parentIsDriveRoot && DriveRootNoRecurse.Contains(name))
                        childDepth = 0;
                    else if (parentIsDriveRoot && DriveRootShallow.Contains(name))
                        childDepth = Math.Min(childDepth, 2);
                    else if (parentIsProfile && name.Equals("AppData", StringComparison.OrdinalIgnoreCase))
                        childDepth = Math.Min(childDepth, 2);
                    else if (name.StartsWith('.'))
                        childDepth = 0; // tool data (.vscode, .docker, .fingerprint…): listed, not crawled

                    var childPath = Path.Join(parentPath, name);
                    if (childDepth > 0 && !IsLink(childPath, attributes))
                        queue.Enqueue((childIndex, childPath, childDepth));

                    if (entries.Count >= MaxEntries)
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
            }

            if (publishIncrementally && entries.Count - lastPublished >= PublishEvery)
            {
                lastPublished = entries.Count;
                _entries = entries.ToArray();
                RaiseIndexChanged();
            }
        }

        _entries = entries.ToArray();
        Interlocked.Exchange(ref _lastBuildUtcTicks, DateTime.UtcNow.Ticks);

        // Folders the watchers saw before this crawl started are now part of the index.
        foreach (var recent in _recentFolders.Values)
        {
            if (recent.AddedTicks < buildStartTicks)
                _recentFolders.TryRemove(recent.Path, out _);
        }

        RaiseIndexChanged();
        SaveCache(_entries, _lastBuildUtcTicks);
    }

    /// <summary>
    /// Depth is only used for ranking (shallower = better). App data and dot-folders count
    /// as much deeper so the user's own folders win over same-named app/tool folders.
    /// </summary>
    private static int ChildDepth(Entry parent, bool parentIsProfile, string name)
    {
        bool penalized = name.StartsWith('.')
                         || (parentIsProfile && name.Equals("AppData", StringComparison.OrdinalIgnoreCase));
        return parent.Depth + (penalized ? 5 : 1);
    }

    /// <summary>
    /// Junctions/symlinks are listed but not followed (avoids loops and double indexing).
    /// Cloud placeholders (OneDrive) are reparse points too but have no link target, so they are crawled.
    /// </summary>
    private static bool IsLink(string path, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReparsePoint) == 0)
            return false;
        try
        {
            return new DirectoryInfo(path).LinkTarget != null;
        }
        catch
        {
            return true;
        }
    }

    private static bool IsDriveRoot(string path)
        => path.Length <= 3 && path.Length >= 2 && path[1] == ':';

    private static List<string> GetCrawlRoots()
    {
        // The user profile goes first so the most relevant folders are indexed earliest.
        var roots = new List<string>();
        if (Directory.Exists(UserProfile))
            roots.Add(UserProfile);

        foreach (var drive in GetFixedDrives())
            roots.Add(drive);
        return roots;
    }

    private static IEnumerable<string> GetFixedDrives()
    {
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch
        {
            yield break;
        }

        foreach (var drive in drives)
        {
            bool ready;
            try
            {
                ready = drive.DriveType == DriveType.Fixed && drive.IsReady;
            }
            catch
            {
                ready = false;
            }
            if (ready)
                yield return drive.RootDirectory.FullName;
        }
    }

    private void LoadKnownFolders()
    {
        var known = new List<KnownFolder>();
        foreach (var (id, fallback) in KnownFolderIds)
        {
            var path = GetKnownFolderPath(id);
            if (path == null || !Directory.Exists(path))
                continue;
            known.Add(CreateKnownFolder(path, fallback));
        }

        var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        if (!string.IsNullOrEmpty(oneDrive) && Directory.Exists(oneDrive))
            known.Add(CreateKnownFolder(oneDrive, "OneDrive"));

        _knownFolders = known.ToArray();
    }

    private static KnownFolder CreateKnownFolder(string path, string fallback)
    {
        var displayName = GetShellDisplayName(path) ?? fallback;
        var keys = new[] { displayName, fallback, Path.GetFileName(path.TrimEnd('\\')) }
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(NormalizeKey)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new KnownFolder(displayName, keys, path);
    }

    private static string? GetKnownFolderPath(Guid id)
    {
        if (NativeMethods.SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var pointer) != 0)
            return null;
        try
        {
            return Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    private static string? GetShellDisplayName(string path)
    {
        var info = new NativeMethods.SHFILEINFO();
        var result = NativeMethods.SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info),
            NativeMethods.SHGFI_DISPLAYNAME);
        return result != IntPtr.Zero && !string.IsNullOrWhiteSpace(info.szDisplayName) ? info.szDisplayName : null;
    }

    // ── Cache ───────────────────────────────────────────────────────────

    private void LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath))
                return;

            using var reader = new StreamReader(CachePath, Encoding.UTF8);
            var header = reader.ReadLine()?.Split('\t');
            if (header is not { Length: 2 } || header[0] != CacheHeader || !long.TryParse(header[1], out var ticks))
                return;

            var entries = new List<Entry>(65_536);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var separator = line.IndexOf('\t');
                if (separator <= 0 || !int.TryParse(line.AsSpan(0, separator), out var parent))
                    return;
                if (parent >= entries.Count)
                    return;

                var name = line[(separator + 1)..];
                var key = NormalizeKey(parent < 0 ? GetDirectoryDisplayName(name) : name);
                var depth = parent < 0
                    ? 0
                    : ChildDepth(entries[parent],
                        entries[parent].Parent < 0 && string.Equals(entries[parent].Name, UserProfile, StringComparison.OrdinalIgnoreCase),
                        name);
                entries.Add(new Entry(name, key == name ? name : key, parent, depth));
            }

            if (_entries.Length == 0)
            {
                _entries = entries.ToArray();
                Interlocked.Exchange(ref _lastBuildUtcTicks, ticks);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FolderIndexService.LoadCache: {ex.Message}");
        }
    }

    private static void SaveCache(Entry[] entries, long buildTicks)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var temp = CachePath + ".tmp";
            using (var writer = new StreamWriter(temp, false, new UTF8Encoding(false)))
            {
                writer.Write(CacheHeader);
                writer.Write('\t');
                writer.WriteLine(buildTicks);
                foreach (var entry in entries)
                {
                    writer.Write(entry.Parent);
                    writer.Write('\t');
                    writer.WriteLine(entry.Name);
                }
            }
            File.Move(temp, CachePath, overwrite: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FolderIndexService.SaveCache: {ex.Message}");
        }
    }

    // ── Watchers ────────────────────────────────────────────────────────

    private void StartWatchers()
    {
        foreach (var root in GetCrawlRoots())
        {
            try
            {
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.DirectoryName,
                    InternalBufferSize = 64 * 1024,
                };
                watcher.Created += (sender, e) => OnFolderCreated(e.FullPath);
                watcher.Renamed += (sender, e) =>
                {
                    _recentFolders.TryRemove(e.OldFullPath, out _);
                    OnFolderCreated(e.FullPath);
                };
                watcher.Deleted += (sender, e) => _recentFolders.TryRemove(e.FullPath, out _);
                watcher.EnableRaisingEvents = true;
                lock (_watchers)
                    _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FolderIndexService.StartWatchers: {ex.Message}");
            }
        }
    }

    private void OnFolderCreated(string path)
    {
        if (_recentFolders.Count >= MaxRecentFolders || IsExcludedPath(path))
            return;

        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name))
            return;

        _recentFolders[path] = new RecentFolder(name, NormalizeKey(name), path, DateTime.UtcNow.Ticks);
        RaiseIndexChanged();
    }

    private static bool IsExcludedPath(string path)
    {
        var segments = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(SkippedNames.Contains) || segments.SkipLast(1).Any(s => s.StartsWith('.')))
            return true;
        if (segments.Length >= 2 && IsDriveRoot(segments[0] + "\\") && DriveRootNoRecurse.Contains(segments[1])
            && !path.StartsWith(UserProfile + "\\", StringComparison.OrdinalIgnoreCase))
            return segments.Length > 2;
        return path.StartsWith(Path.Join(UserProfile, "AppData") + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private void RaiseIndexChanged()
    {
        try
        {
            IndexChanged?.Invoke();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FolderIndexService.IndexChanged: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _disposeCts.Cancel();
        lock (_watchers)
        {
            foreach (var watcher in _watchers)
                watcher.Dispose();
            _watchers.Clear();
        }
    }
}
