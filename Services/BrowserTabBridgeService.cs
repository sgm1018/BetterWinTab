using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using BetterWinTab.Models;

namespace BetterWinTab.Services;

/// <summary>
/// Receives browser tab updates from the Chromium Native Messaging host and
/// sends activation commands back to the extension.
/// </summary>
public sealed class BrowserTabBridgeService : IDisposable
{
    private const string PipeName = "BetterWinTab.BrowserTabs";
    private readonly ConcurrentDictionary<string, BrowserConnection> _connections = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentDictionary<string, List<BridgeTab>> _tabs = new(StringComparer.OrdinalIgnoreCase);

    public event Action? TabsChanged;
    public event Action? StatusChanged;

    public string GetHealthSummary()
    {
        var supported = new[] { "Chrome", "Edge", "Brave" };
        var states = supported.Select(browser =>
        {
            if (!_connections.ContainsKey(browser))
                return $"{browser}: not connected";

            var count = _tabs.TryGetValue(browser, out var tabs) ? tabs.Count : 0;
            return $"{browser}: connected ({count} tabs)";
        });

        return string.Join(" · ", states);
    }

    public BrowserTabBridgeService()
    {
        _ = AcceptConnectionsAsync();
    }

    public List<WindowInfo> GetTabs(BrowserTabSettings settings, IReadOnlyList<WindowInfo> windows)
    {
        if (!settings.Enabled) return new List<WindowInfo>();

        var enabledBrowsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (settings.ChromeEnabled) enabledBrowsers.Add("Chrome");
        if (settings.EdgeEnabled) enabledBrowsers.Add("Edge");
        if (settings.BraveEnabled) enabledBrowsers.Add("Brave");

        var result = new List<WindowInfo>();
        foreach (var pair in _tabs)
        {
            if (!enabledBrowsers.Contains(pair.Key)) continue;

            var browserProcess = ProcessNameFor(pair.Key);
            var browserWindows = windows
                .Where(window => window.ProcessName.Equals(browserProcess, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var groups = pair.Value.GroupBy(tab => tab.WindowId).ToList();
            var matches = groups.Select(tabs =>
                (Tabs: tabs, Window: FindBrowserWindow(browserWindows, tabs, groups.Count == 1))).ToList();

            foreach (var (tabs, match) in matches)
            {
                // Keep ambiguous tabs searchable and let Chromium activate them, but don't
                // associate them with an HWND that might belong to a different browser window.
                var window = match != null && matches.Count(other => other.Window?.Handle == match.Handle) == 1
                    ? match
                    : null;
                foreach (var tab in tabs)
                {
                    result.Add(new WindowInfo
                    {
                        Handle = window?.Handle ?? IntPtr.Zero,
                        Title = string.IsNullOrWhiteSpace(tab.Title) ? tab.Url : tab.Title,
                        ProcessName = browserProcess,
                        ProcessId = window?.ProcessId ?? tab.BrowserProcessId,
                        ClassName = window?.ClassName ?? string.Empty,
                        IsMinimized = window?.IsMinimized ?? false,
                        IsOnCurrentDesktop = window?.IsOnCurrentDesktop ?? true,
                        DesktopId = window?.DesktopId ?? Guid.Empty,
                        DesktopNumber = window?.DesktopNumber ?? 0,
                        DesktopName = window?.DesktopName ?? string.Empty,
                        IsBrowserTab = true,
                        BrowserName = pair.Key,
                        BrowserTabId = tab.Id,
                        BrowserWindowId = tab.WindowId,
                        BrowserProcessId = tab.BrowserProcessId,
                        FaviconUrl = tab.FaviconUrl,
                        Url = tab.Url
                    });
                }
            }
        }

        return result;
    }

    public bool ActivateTab(WindowInfo tab)
    {
        if (!tab.IsBrowserTab || !_connections.TryGetValue(tab.BrowserName, out var connection))
            return false;

        return connection.Send(new
        {
            type = "activate",
            tabId = tab.BrowserTabId,
            windowId = tab.BrowserWindowId
        });
    }

    public void RequestTabsRefresh()
    {
        foreach (var connection in _connections.Values.Distinct())
            connection.Send(new { type = "refresh" });
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        foreach (var connection in _connections.Values)
            connection.Dispose();
        _shutdown.Dispose();
    }

    private async Task AcceptConnectionsAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            try
            {
                await pipe.WaitForConnectionAsync(_shutdown.Token);
                _ = HandleConnectionAsync(pipe);
            }
            catch (OperationCanceledException)
            {
                pipe.Dispose();
                return;
            }
            catch
            {
                pipe.Dispose();
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe)
    {
        using (pipe)
        {
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        BrowserConnection? connection = null;

        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(_shutdown.Token);
                if (line == null) break;

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var browser = root.TryGetProperty("browser", out var browserValue)
                    ? NormalizeBrowserName(browserValue.GetString())
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(browser)) continue;

                if (connection == null)
                {
                    connection = new BrowserConnection(writer);
                    connection.Send(new { type = "refresh" });
                }

                _connections[browser] = connection;

                if (root.TryGetProperty("type", out var type) && type.GetString() == "tabs")
                {
                    var tabs = ParseTabs(root);
                    var changed = !_tabs.TryGetValue(browser, out var previousTabs)
                        || !previousTabs.SequenceEqual(tabs);
                    _tabs[browser] = tabs;
                    if (changed)
                        TabsChanged?.Invoke();
                    StatusChanged?.Invoke();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"BrowserTabBridgeService: {ex.Message}");
        }
        finally
        {
            if (connection != null)
            {
                foreach (var pair in _connections.Where(pair => ReferenceEquals(pair.Value, connection)).ToList())
                {
                    _connections.TryRemove(pair.Key, out _);
                    _tabs.TryRemove(pair.Key, out _);
                    StatusChanged?.Invoke();
                }
            }
        }
        }
    }

    private static List<BridgeTab> ParseTabs(JsonElement root)
    {
        var result = new List<BridgeTab>();
        if (!root.TryGetProperty("tabs", out var tabs)) return result;
        var processId = root.TryGetProperty("browserProcessId", out var process)
            ? process.GetInt32()
            : 0;

        foreach (var item in tabs.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var id)) continue;
            result.Add(new BridgeTab(
                id.ToString(),
                item.TryGetProperty("windowId", out var windowId) ? windowId.ToString() : string.Empty,
                ReadString(item, "title"),
                ReadString(item, "url"),
                ReadString(item, "favIconUrl"),
                processId,
                item.TryGetProperty("active", out var active) && active.ValueKind == JsonValueKind.True));
        }

        return result;
    }

    private static WindowInfo? FindBrowserWindow(
        IReadOnlyList<WindowInfo> windows, IEnumerable<BridgeTab> tabs, bool onlyBrowserWindow)
    {
        var processId = tabs.First().BrowserProcessId;
        if (processId > 0)
        {
            var processWindows = windows.Where(window => window.ProcessId == processId).ToList();
            if (processWindows.Count > 0) windows = processWindows;
        }

        if (onlyBrowserWindow && windows.Count == 1) return windows[0];

        // Chromium shows the active tab's title in the native caption. Older messages
        // may omit the active flag, in which case accept only a single matching HWND.
        var activeTab = tabs.FirstOrDefault(tab => tab.IsActive && !string.IsNullOrWhiteSpace(tab.Title));
        var titles = activeTab != null
            ? new[] { activeTab.Title }
            : tabs.Select(tab => tab.Title).Where(title => !string.IsNullOrWhiteSpace(title)).ToArray();
        var matches = windows.Where(window => titles.Any(title =>
                window.Title.Equals(title, StringComparison.OrdinalIgnoreCase) ||
                window.Title.StartsWith(title + " - ", StringComparison.OrdinalIgnoreCase)))
            .Take(2).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static string ProcessNameFor(string browser) => browser switch
    {
        "Edge" => "msedge",
        "Brave" => "brave",
        _ => "chrome"
    };

    private static string NormalizeBrowserName(string? browser) =>
        browser?.Trim().ToLowerInvariant() switch
        {
            var value when value is not null && value.Contains("edge") => "Edge",
            var value when value is not null && value.Contains("brave") => "Brave",
            var value when value is not null && value.Contains("chrome") => "Chrome",
            _ => string.Empty
        };

    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private sealed record BridgeTab(
        string Id, string WindowId, string Title, string Url, string FaviconUrl, int BrowserProcessId, bool IsActive);

    private sealed class BrowserConnection : IDisposable
    {
        private readonly StreamWriter _writer;
        private readonly object _sync = new();

        public BrowserConnection(StreamWriter writer) => _writer = writer;

        public bool Send(object message)
        {
            try
            {
                lock (_sync)
                    _writer.WriteLine(JsonSerializer.Serialize(message));
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            try { _writer.Dispose(); } catch { }
        }
    }
}
