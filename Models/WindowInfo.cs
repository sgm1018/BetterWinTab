namespace BetterWinTab.Models;

/// <summary>
/// Represents information about an open window.
/// </summary>
public class WindowInfo
{
    public IntPtr Handle { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string ClassName { get; set; } = string.Empty;
    public string? IconPath { get; set; }
    public bool IsMinimized { get; set; }
    public DateTime LastActiveTime { get; set; } = DateTime.Now;

    // ── Virtual Desktop ────────────────────────────────────
    /// <summary>Desktop GUID from IVirtualDesktopManager.</summary>
    public Guid DesktopId { get; set; } = Guid.Empty;

    /// <summary>1-based desktop number (1 = first desktop).</summary>
    public int DesktopNumber { get; set; }

    /// <summary>Human-readable desktop name (e.g. "Escritorio 2" or a custom name).</summary>
    public string DesktopName { get; set; } = string.Empty;

    /// <summary>True when the window lives on the currently active virtual desktop.</summary>
    public bool IsOnCurrentDesktop { get; set; } = true;

    /// <summary>True when the user has pinned/favorited this window.</summary>
    public bool IsPinned { get; set; } = false;

    public override bool Equals(object? obj) =>
        obj is WindowInfo other && Handle == other.Handle;

    public override int GetHashCode() => Handle.GetHashCode();

    public override string ToString() => $"{ProcessName}: {Title}";
}
