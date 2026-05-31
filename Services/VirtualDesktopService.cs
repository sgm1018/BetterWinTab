using System.Runtime.InteropServices;
using BetterWinTab.Interop;
using BetterWinTab.Models;
using Microsoft.Win32;

namespace BetterWinTab.Services;

/// <summary>
/// Wraps the Windows IVirtualDesktopManager COM interface to detect which
/// virtual desktop each window belongs to and to move windows between desktops.
/// Falls back gracefully when virtual desktops are unavailable.
/// </summary>
public class VirtualDesktopService
{
    private readonly IVirtualDesktopManager? _manager;

    /// <summary>
    /// Ordered list of desktop GUIDs discovered during the last enrichment pass.
    /// Index 0 = desktop 1, etc.
    /// </summary>
    private readonly List<Guid> _knownDesktops = new();

    /// <summary>The GUID of the desktop that was active during the last enrichment.</summary>
    private Guid _currentDesktopId = Guid.Empty;

    /// <summary>Handle of our overlay window, used to reliably detect the current desktop GUID.</summary>
    private IntPtr _ownerHwnd;

    public VirtualDesktopService()
    {
        try
        {
            _manager = (IVirtualDesktopManager)new VirtualDesktopManagerClass();
        }
        catch
        {
            // COM activation can fail on older Windows builds or in restricted environments.
            _manager = null;
        }
    }

    /// <summary>
    /// Provides the overlay window handle so EnrichWindows can determine the current virtual desktop
    /// reliably even before any user windows are enumerated.
    /// </summary>
    public void SetOwnerHwnd(IntPtr hwnd) => _ownerHwnd = hwnd;

    /// <summary>
    /// Whether the service is available (COM interface loaded successfully).
    /// </summary>
    public bool IsAvailable => _manager != null;

    /// <summary>
    /// True when the system has more than one virtual desktop.
    /// Used by the UI to decide whether to show desktop badges at all.
    /// </summary>
    public bool HasMultipleDesktops => _knownDesktops.Count > 1;

    /// <summary>
    /// Enriches a list of <see cref="WindowInfo"/> with virtual desktop metadata:
    /// DesktopId, DesktopNumber, and IsOnCurrentDesktop.
    /// </summary>
    public void EnrichWindows(List<WindowInfo> windows)
    {
        if (_manager == null) return;

        _currentDesktopId = Guid.Empty;

        // Determine the current desktop from our overlay HWND first —
        // much more reliable than inferring it from enumerated user windows.
        if (_ownerHwnd != IntPtr.Zero)
        {
            try
            {
                int hr = _manager.GetWindowDesktopId(_ownerHwnd, out var ownerDesktopId);
                if (hr == 0 && ownerDesktopId != Guid.Empty)
                    _currentDesktopId = ownerDesktopId;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"VirtualDesktopService.GetOwnerDesktopId: {ex.Message}"); }
        }

        foreach (var w in windows)
        {
            try
            {
                int hr = _manager.GetWindowDesktopId(w.Handle, out var desktopId);
                if (hr == 0 && desktopId != Guid.Empty)
                {
                    w.DesktopId = desktopId;

                    hr = _manager.IsWindowOnCurrentVirtualDesktop(w.Handle, out bool onCurrent);
                    if (hr == 0)
                    {
                        w.IsOnCurrentDesktop = onCurrent;
                    }
                    else if (_currentDesktopId != Guid.Empty)
                    {
                        // IsWindowOnCurrentVirtualDesktop can return E_FAIL for windows on other
                        // desktops in some Windows builds. Fall back to GUID comparison.
                        w.IsOnCurrentDesktop = (desktopId == _currentDesktopId);
                    }
                    else
                    {
                        w.IsOnCurrentDesktop = true; // safe default
                    }

                    // Infer current desktop from the first window confirmed to be on it
                    if (w.IsOnCurrentDesktop && _currentDesktopId == Guid.Empty)
                        _currentDesktopId = desktopId;
                }
                else
                {
                    w.DesktopId = Guid.Empty;
                    w.IsOnCurrentDesktop = true; // assume current if API fails
                }
            }
            catch
            {
                w.DesktopId = Guid.Empty;
                w.IsOnCurrentDesktop = true;
            }
        }

        // Build ordered desktop list from discovered GUIDs
        RebuildDesktopIndex(windows);

        // Assign 1-based desktop numbers and names
        var nameCache = new Dictionary<Guid, string>();
        foreach (var w in windows)
        {
            int idx = _knownDesktops.IndexOf(w.DesktopId);
            w.DesktopNumber = idx >= 0 ? idx + 1 : 0;

            if (w.DesktopId != Guid.Empty)
            {
                if (!nameCache.TryGetValue(w.DesktopId, out var name))
                {
                    name = GetDesktopName(w.DesktopId, w.DesktopNumber);
                    nameCache[w.DesktopId] = name;
                }
                w.DesktopName = name;
            }
        }
    }

    /// <summary>
    /// Returns the user-visible name for a virtual desktop.
    /// Reads from the registry; falls back to a locale-aware "Desktop {number}" when no name is stored.
    /// </summary>
    private static string GetDesktopName(Guid desktopId, int desktopNumber)
    {
        try
        {
            var keyPath = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops\Desktops\{{{desktopId}}}";
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
            if (key != null)
            {
                var name = key.GetValue("Name") as string;
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"VirtualDesktopService.GetDesktopName: {ex.Message}"); }

        // Fall back to a locale-aware positional label matching what Windows Task View shows
        return desktopNumber > 0 ? GetLocalizedDefaultDesktopName(desktopNumber) : string.Empty;
    }

    /// <summary>
    /// Returns the localized word for "Desktop" based on the current UI culture,
    /// matching the default desktop names that Windows Task View displays.
    /// </summary>
    internal static string GetLocalizedDefaultDesktopName(int number)
    {
        var lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var word = lang switch
        {
            "es" => "Escritorio",
            "fr" => "Bureau",
            "de" => "Schreibtisch",
            "it" => "Scrivania",
            "pt" => "Área de Trabalho",
            "ja" => "デスクトップ",
            "zh" => "桌面",
            "ko" => "바탕 화면",
            "nl" => "Bureaublad",
            "ru" => "Рабочий стол",
            "pl" => "Pulpit",
            "tr" => "Masaüstü",
            "ar" => "سطح المكتب",
            "sv" => "Skrivbord",
            "nb" or "no" => "Skrivebord",
            "da" => "Skrivebord",
            "fi" => "Työpöytä",
            "cs" => "Plocha",
            "hu" => "Asztal",
            _ => "Desktop"
        };
        return $"{word} {number}";
    }

    /// <summary>
    /// Switches to the virtual desktop that contains <paramref name="hWnd"/>
    /// by moving it to the current desktop (the only documented COM operation).
    /// After moving, calls SetForegroundWindow to activate it.
    /// </summary>
    /// <returns>true if the window was moved or was already on the current desktop.</returns>
    public bool SwitchToWindowDesktop(IntPtr hWnd)
    {
        if (_manager == null) return false;

        try
        {
            int hr = _manager.IsWindowOnCurrentVirtualDesktop(hWnd, out bool onCurrent);
            if (hr == 0 && onCurrent)
                return true; // already here

            // Move the window to the current desktop
            if (_currentDesktopId != Guid.Empty)
            {
                var desktopId = _currentDesktopId;
                hr = _manager.MoveWindowToDesktop(hWnd, ref desktopId);
                return hr == 0;
            }
        }
        catch
        {
            // COM can throw on invalid handles
        }

        return false;
    }

    /// <summary>
    /// Rebuilds the ordered desktop index using the TRUE order from the
    /// Windows registry <c>VirtualDesktopIDs</c> blob, which matches what
    /// Windows Task View displays as Desktop 1, Desktop 2, etc.
    /// Falls back to inferring order from enumerated windows when the blob
    /// is unavailable.
    /// </summary>
    private void RebuildDesktopIndex(List<WindowInfo> windows)
    {
        _knownDesktops.Clear();

        // Primary path: read the authoritative ordered list from Windows
        var orderedIds = ReadOrderedDesktopIds();
        if (orderedIds.Count > 0)
        {
            _knownDesktops.AddRange(orderedIds);

            // Safety net: add any GUID found in windows that wasn't in the blob
            var knownSet = new HashSet<Guid>(_knownDesktops);
            foreach (var w in windows)
            {
                if (w.DesktopId != Guid.Empty && knownSet.Add(w.DesktopId))
                    _knownDesktops.Add(w.DesktopId);
            }
            return;
        }

        // Fallback: infer order from enumerated windows (pre-blob behavior)
        var unique = windows
            .Select(w => w.DesktopId)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        // Current desktop first so it gets the lowest number
        if (_currentDesktopId != Guid.Empty && unique.Contains(_currentDesktopId))
        {
            _knownDesktops.Add(_currentDesktopId);
            unique.Remove(_currentDesktopId);
        }

        unique.Sort();
        _knownDesktops.AddRange(unique);
    }

    /// <summary>
    /// Reads the ordered list of active virtual desktop GUIDs from the
    /// <c>HKCU\...\VirtualDesktops\VirtualDesktopIDs</c> binary registry value.
    /// Each desktop GUID is stored as 16 bytes in Windows mixed-endian format.
    /// </summary>
    private static List<Guid> ReadOrderedDesktopIds()
    {
        var result = new List<Guid>();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops");
            if (key == null) return result;

            var blob = key.GetValue("VirtualDesktopIDs") as byte[];
            if (blob == null || blob.Length == 0 || blob.Length % 16 != 0)
                return result;

            var bytes = new byte[16];
            for (int i = 0; i < blob.Length; i += 16)
            {
                Array.Copy(blob, i, bytes, 0, 16);
                result.Add(new Guid(bytes));
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"VirtualDesktopService.ReadOrderedDesktopIds: {ex.Message}"); }
        return result;
    }
}
