using System.Diagnostics;
using BetterWinTab.Models;
using BetterWinTab.Interop;
using static BetterWinTab.Interop.NativeMethods;

namespace BetterWinTab.Services;

/// <summary>
/// Enumerates and manages open windows using Win32 APIs.
/// </summary>
public class WindowEnumerationService
{
    private static readonly HashSet<string> ExcludedClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "WorkerW",
        "Windows.UI.Core.CoreWindow",
        "ApplicationFrameTitleBarWindow",
        "ForegroundStaging",
        "MultitaskingViewFrame",
        "SHELLDLL_DefView"
    };

    private static readonly HashSet<string> ExcludedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "SearchUI",
        "ShellExperienceHost",
        "StartMenuExperienceHost",
        "SearchHost",
        "TextInputHost",
        "LockApp",
        "SystemSettings",
        "BetterWinTab"  // Exclude our own overlay window
    };

    /// <summary>
    /// Gets all visible top-level windows that represent real user applications.
    /// </summary>
    public List<WindowInfo> GetAllWindows()
    {
        var windows = new List<WindowInfo>();
        var shellWindow = NativeMethods.GetShellWindow();
        var desktopWindow = NativeMethods.GetDesktopWindow();

        NativeMethods.EnumWindows((hWnd, lParam) =>
        {
            // Skip shell and desktop
            if (hWnd == shellWindow || hWnd == desktopWindow)
                return true;

            // Must be visible
            if (!NativeMethods.IsWindowVisible(hWnd))
                return true;

            // Get window title
            var titleBuffer = new char[512];
            int titleLen = NativeMethods.GetWindowText(hWnd, titleBuffer, titleBuffer.Length);
            if (titleLen == 0)
                return true;

            string title = new string(titleBuffer, 0, titleLen);

            // Get class name
            var classBuffer = new char[256];
            int classLen = NativeMethods.GetClassName(hWnd, classBuffer, classBuffer.Length);
            string className = classLen > 0 ? new string(classBuffer, 0, classLen) : string.Empty;

            // Skip excluded classes
            if (ExcludedClasses.Contains(className))
                return true;

            // Check window styles — skip tool windows and child windows
            uint exStyle = (uint)NativeMethods.GetWindowLong(hWnd, GWL_EXSTYLE);
            if ((exStyle & WS_EX_TOOLWINDOW) != 0)
                return true;

            // Skip if it's not an app window and has an owner
            var owner = NativeMethods.GetWindow(hWnd, GW_OWNER);
            if (owner != IntPtr.Zero && (exStyle & WS_EX_APPWINDOW) == 0)
                return true;

            // Get process info
            NativeMethods.GetWindowThreadProcessId(hWnd, out uint processId);

            string processName = string.Empty;
            try
            {
                var process = Process.GetProcessById((int)processId);
                processName = process.ProcessName;
            }
            catch
            {
                // Process may have exited
            }

            // Skip excluded processes
            if (ExcludedProcesses.Contains(processName))
                return true;

            bool isMinimized = NativeMethods.IsIconic(hWnd);

            windows.Add(new WindowInfo
            {
                Handle = hWnd,
                Title = title,
                ProcessName = processName,
                ProcessId = (int)processId,
                ClassName = className,
                IsMinimized = isMinimized,
                LastActiveTime = DateTime.Now
            });

            return true;
        }, IntPtr.Zero);

        return windows;
    }

    /// <summary>
    /// Gets windows filtered by process name.
    /// </summary>
    public List<WindowInfo> GetWindowsByProcess(string processName)
    {
        return GetAllWindows()
            .Where(w => w.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Gets windows filtered by class name.
    /// </summary>
    public List<WindowInfo> GetWindowsByClassName(string className)
    {
        return GetAllWindows()
            .Where(w => w.ClassName.Equals(className, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Switches to (activates/restores) the specified window.
    /// </summary>
    public void SwitchToWindow(IntPtr hWnd)
    {
        if (NativeMethods.IsIconic(hWnd))
        {
            NativeMethods.ShowWindow(hWnd, SW_RESTORE);
        }

        NativeMethods.SetForegroundWindow(hWnd);
    }

    /// <summary>
    /// Gets a list of all unique process names with open windows.
    /// </summary>
    public List<string> GetRunningProcessNames()
    {
        return GetAllWindows()
            .Select(w => w.ProcessName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();
    }
}
