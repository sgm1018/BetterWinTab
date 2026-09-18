using System.Net;
using System.Text.Json;
using BetterWinTab.Interop;
using BetterWinTab.Models;

namespace BetterWinTab.Services;

/// <summary>
/// Reads and activates tabs exposed by Chromium browsers through their local
/// DevTools HTTP endpoint. Browsers must be started with remote debugging enabled.
/// </summary>
public class BrowserTabService
{
    private readonly BrowserTabBridgeService _bridge;

    public BrowserTabService(BrowserTabBridgeService bridge)
    {
        _bridge = bridge;
    }

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMilliseconds(350)
    };

    private static readonly BrowserEndpoint[] Endpoints =
    {
        new("Chrome", "chrome", settings => settings.ChromeEnabled, settings => settings.ChromePort),
        new("Edge", "msedge", settings => settings.EdgeEnabled, settings => settings.EdgePort),
        new("Brave", "brave", settings => settings.BraveEnabled, settings => settings.BravePort)
    };

    public List<WindowInfo> GetTabs(BrowserTabSettings settings)
    {
        var bridgedTabs = _bridge.GetTabs(settings);
        if (bridgedTabs.Count > 0 || settings.Enabled)
            return bridgedTabs;

        var tabs = new List<WindowInfo>();
        if (!settings.Enabled) return tabs;

        foreach (var endpoint in Endpoints)
        {
            if (!endpoint.IsEnabled(settings)) continue;
            tabs.AddRange(GetTabs(endpoint, endpoint.GetPort(settings)));
        }

        return tabs;
    }

    public bool ActivateTab(WindowInfo tab)
    {
        if (tab.IsBrowserTab && tab.BrowserDebugPort == 0)
            return _bridge.ActivateTab(tab);

        if (!tab.IsBrowserTab || string.IsNullOrEmpty(tab.BrowserTabId)) return false;

        try
        {
            var url = $"http://127.0.0.1:{tab.BrowserDebugPort}/json/activate/{Uri.EscapeDataString(tab.BrowserTabId)}";
            using var response = Http.GetAsync(url).GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<WindowInfo> GetTabs(BrowserEndpoint endpoint, int port)
    {
        if (port is < 1 or > 65535) yield break;

        string json;
        try
        {
            json = Http.GetStringAsync($"http://127.0.0.1:{port}/json/list")
                .GetAwaiter().GetResult();
        }
        catch
        {
            yield break;
        }

        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(json);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("type", out var type) || type.GetString() != "page")
                    continue;
                if (!item.TryGetProperty("id", out var id)) continue;

                var tabId = id.GetString();
                if (string.IsNullOrWhiteSpace(tabId)) continue;

                var title = ReadString(item, "title");
                var url = ReadString(item, "url");
                var parentWindow = FindBrowserWindow(endpoint.ProcessName, title);
                if (parentWindow == null) continue;

                yield return new WindowInfo
                {
                    Handle = parentWindow.Handle,
                    Title = string.IsNullOrWhiteSpace(title) ? url : title,
                    ProcessName = endpoint.ProcessName,
                    ProcessId = parentWindow.ProcessId,
                    ClassName = parentWindow.ClassName,
                    IsMinimized = parentWindow.IsMinimized,
                    IsOnCurrentDesktop = parentWindow.IsOnCurrentDesktop,
                    DesktopId = parentWindow.DesktopId,
                    DesktopNumber = parentWindow.DesktopNumber,
                    DesktopName = parentWindow.DesktopName,
                    IsBrowserTab = true,
                    BrowserName = endpoint.Name,
                    BrowserTabId = tabId,
                    BrowserWindowId = ReadString(item, "webSocketDebuggerUrl"),
                    BrowserDebugPort = port,
                    Url = url
                };
            }
        }
        finally
        {
            document?.Dispose();
        }
    }

    private static WindowInfo? FindBrowserWindow(string processName, string tabTitle)
    {
        var windows = ServiceContainer.Resolve<WindowEnumerationService>()
            .GetWindowsByProcess(processName);
        if (windows.Count == 0) return null;

        return windows.FirstOrDefault(window =>
                   window.Title.Contains(tabTitle, StringComparison.OrdinalIgnoreCase))
               ?? windows[0];
    }

    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private sealed record BrowserEndpoint(
        string Name,
        string ProcessName,
        Func<BrowserTabSettings, bool> IsEnabled,
        Func<BrowserTabSettings, int> GetPort);
}