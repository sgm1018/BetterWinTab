using System.Diagnostics;
using System.Runtime.InteropServices;
using BetterWinTab.Interop;
using Microsoft.UI.Dispatching;

namespace BetterWinTab.Services;

/// <summary>
/// Global hotkey service using a Low-Level Keyboard Hook (WH_KEYBOARD_LL).
/// This approach is reliable for Ctrl+Tab, unlike RegisterHotKey which
/// Windows often reserves for internal use.
/// </summary>
public class HotkeyService : IDisposable
{
    private IntPtr _hookId = IntPtr.Zero;
    private NativeMethods.LowLevelKeyboardProc? _hookProc;
    private DispatcherQueue? _dispatcherQueue;
    private bool _disposed;

    // Configurable hotkey: defaults to Ctrl+Tab
    private uint _configuredVKey = NativeMethods.VK_TAB;
    private uint _configuredModifiers = 0x0002; // MOD_CONTROL

    // Debounce: prevent rapid-fire toggles
    private DateTime _lastToggle = DateTime.MinValue;
    private static readonly TimeSpan ToggleCooldown = TimeSpan.FromMilliseconds(300);

    public event Action? HotkeyPressed;

    /// <summary>
    /// Fired when Alt+Tab or Win+Tab is pressed while the overlay is visible.
    /// The key is NOT consumed — Windows handles the switcher normally.
    /// </summary>
    public event Action? HideOverlayRequested;

    /// <summary>
    /// Delegate that returns true when the BetterWinTab overlay is currently visible.
    /// Set by the App layer so the hook can determine whether to fire HideOverlayRequested.
    /// </summary>
    public Func<bool>? IsOverlayVisible;

    /// <summary>
    /// Updates the key combination that triggers the hotkey.
    /// </summary>
    /// <param name="modifiers">Bitmask: MOD_CTRL=0x0002, MOD_ALT=0x0001, MOD_SHIFT=0x0004, MOD_WIN=0x0008</param>
    /// <param name="vKey">Virtual key code (e.g. VK_TAB = 0x09)</param>
    public void Configure(uint modifiers, uint vKey)
    {
        _configuredModifiers = modifiers;
        _configuredVKey      = vKey;
    }

    /// <summary>
    /// Installs the low-level keyboard hook. Call from UI thread.
    /// </summary>
    public bool Install(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;

        // MUST keep a reference to prevent GC from collecting the delegate
        _hookProc = HookCallback;

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        var moduleHandle = NativeMethods.GetModuleHandle(curModule.ModuleName);

        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _hookProc,
            moduleHandle,
            0);

        return _hookId != IntPtr.Zero;
    }

    /// <summary>
    /// Low-level keyboard hook callback.
    /// Detects Ctrl+Tab and fires HotkeyPressed on the UI thread.
    /// </summary>
    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)NativeMethods.WM_KEYDOWN || wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN))
        {
            var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);

            // ── Configured hotkey (Ctrl+Tab by default) ──
            if (hookStruct.vkCode == _configuredVKey)
            {
                if (CheckModifiers(_configuredModifiers))
                {
                    var now = DateTime.UtcNow;
                    if (now - _lastToggle > ToggleCooldown)
                    {
                        _lastToggle = now;
                        _dispatcherQueue?.TryEnqueue(() => HotkeyPressed?.Invoke());
                    }

                    // Consume the keystroke so it doesn't reach other apps
                    return (IntPtr)1;
                }
            }

            // ── Hide overlay on Alt+Tab or Win+Tab (pass keystroke through to Windows) ──
            if (hookStruct.vkCode == NativeMethods.VK_TAB)
            {
                bool isAltTab = wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN;
                bool isWinTab = wParam == (IntPtr)NativeMethods.WM_KEYDOWN &&
                                ((NativeMethods.GetAsyncKeyState(0x5B) & 0x8000) != 0 ||  // VK_LWIN
                                 (NativeMethods.GetAsyncKeyState(0x5C) & 0x8000) != 0);  // VK_RWIN

                if ((isAltTab || isWinTab) && IsOverlayVisible?.Invoke() == true)
                {
                    _dispatcherQueue?.TryEnqueue(() => HideOverlayRequested?.Invoke());
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    /// <summary>
    /// Returns true when the modifier keys required by <paramref name="modifiers"/> are all pressed.
    /// </summary>
    private static bool CheckModifiers(uint modifiers)
    {
        bool needCtrl  = (modifiers & 0x0002) != 0;
        bool needAlt   = (modifiers & 0x0001) != 0;
        bool needShift = (modifiers & 0x0004) != 0;
        bool needWin   = (modifiers & 0x0008) != 0;

        bool ctrlDown  = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LCONTROL) & 0x8000) != 0
                      || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RCONTROL) & 0x8000) != 0;
        bool altDown   = (NativeMethods.GetAsyncKeyState(0x12) & 0x8000) != 0; // VK_MENU
        bool shiftDown = (NativeMethods.GetAsyncKeyState(0x10) & 0x8000) != 0; // VK_SHIFT
        bool winDown   = (NativeMethods.GetAsyncKeyState(0x5B) & 0x8000) != 0  // VK_LWIN
                      || (NativeMethods.GetAsyncKeyState(0x5C) & 0x8000) != 0; // VK_RWIN

        if (needCtrl  && !ctrlDown)  return false;
        if (needAlt   && !altDown)   return false;
        if (needShift && !shiftDown) return false;
        if (needWin   && !winDown)   return false;
        return true;
    }

    /// <summary>
    /// Removes the keyboard hook.
    /// </summary>
    public void Uninstall()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Uninstall();
            _hookProc = null;
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
