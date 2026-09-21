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
    private readonly WindowEnumerationService _windowService;
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

    public BrowserTabBridgeService(WindowEnumerationService windowService)
    {
        _windowService = windowService;
        _ = AcceptConnectionsAsync();
    }

    public List<WindowInfo> GetTabs(BrowserTabSettings settings)
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
            foreach (var tab in pair.Value)
            {
                var browserProcess = ProcessNameFor(pair.Key);
                var window = FindBrowserWindow(browserProcess, tab.BrowserProcessId, tab.Title);
                if (window == null) continue;

                result.Add(new WindowInfo
                {
                    Handle = window.Handle,
                    Title = string.IsNullOrWhiteSpace(tab.Title) ? tab.Url : tab.Title,
                    ProcessName = browserProcess,
                    ProcessId = window.ProcessId,
                    ClassName = window.ClassName,
                    IsMinimized = window.IsMinimized,
                    IsOnCurrentDesktop = window.IsOnCurrentDesktop,
                    DesktopId = window.DesktopId,
                    DesktopNumber = window.DesktopNumber,
                    DesktopName = window.DesktopName,
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

                connection ??= new BrowserConnection(writer);
                _connections[browser] = connection;

                if (root.TryGetProperty("type", out var type) && type.GetString() == "tabs")
                {
                    var tabs = ParseTabs(root, browser);
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

    private List<BridgeTab> ParseTabs(JsonElement root, string browser)
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
                processId));
        }

        return result;
    }

    private WindowInfo? FindBrowserWindow(string processName, int processId, string tabTitle)
    {
        var windows = _windowService.GetAllWindows()
            .Where(window => window.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (processId > 0)
        {
            var processWindows = windows.Where(window => window.ProcessId == processId).ToList();
            if (processWindows.Count > 0) windows = processWindows;
        }

        return windows.FirstOrDefault(window =>
                   !string.IsNullOrWhiteSpace(tabTitle) &&
                   window.Title.Contains(tabTitle, StringComparison.OrdinalIgnoreCase))
               ?? windows.FirstOrDefault();
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

    private sealed record BridgeTab(string Id, string WindowId, string Title, string Url, string FaviconUrl, int BrowserProcessId);

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
