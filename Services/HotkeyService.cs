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

    // Low-level hook callbacks run before Windows updates the asynchronous key state.
    // Track modifier transitions here and latch each hotkey press until its key-up so
    // key-repeat cannot toggle repeatedly and quick taps are never dropped by a timer.
    private readonly HashSet<uint> _pressedModifierKeys = new();
    private bool _hotkeyKeyDown;

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

        foreach (uint key in new uint[]
                 {
                     NativeMethods.VK_LCONTROL, NativeMethods.VK_RCONTROL,
                     0xA4, 0xA5, // VK_LMENU / VK_RMENU
                     0xA0, 0xA1, // VK_LSHIFT / VK_RSHIFT
                     0x5B, 0x5C  // VK_LWIN / VK_RWIN
                 })
        {
            if (IsKeyDown((int)key))
                _pressedModifierKeys.Add(key);
        }

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
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            bool isKeyDown = wParam == (IntPtr)NativeMethods.WM_KEYDOWN ||
                             wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN;
            bool isKeyUp = wParam == (IntPtr)NativeMethods.WM_KEYUP ||
                           wParam == (IntPtr)NativeMethods.WM_SYSKEYUP;

            if (isKeyDown || isKeyUp)
                UpdateModifierState(hookStruct.vkCode, isKeyDown);

            if (isKeyUp && hookStruct.vkCode == _configuredVKey && _hotkeyKeyDown)
            {
                _hotkeyKeyDown = false;
                // Pair the consumed hotkey key-down with a consumed key-up.
                return (IntPtr)1;
            }

            // ── Configured hotkey (Ctrl+Tab by default) ──
            if (isKeyDown && hookStruct.vkCode == _configuredVKey)
            {
                if (CheckModifiers(_configuredModifiers))
                {
                    if (!_hotkeyKeyDown)
                    {
                        _hotkeyKeyDown = true;
                        _dispatcherQueue?.TryEnqueue(() => HotkeyPressed?.Invoke());
                    }

                    // Consume the keystroke so it doesn't reach other apps
                    return (IntPtr)1;
                }
            }

            // ── Hide overlay on Alt+Tab or Win+Tab (pass keystroke through to Windows) ──
            if (isKeyDown && hookStruct.vkCode == NativeMethods.VK_TAB)
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
    private bool CheckModifiers(uint modifiers)
    {
        bool needCtrl  = (modifiers & 0x0002) != 0;
        bool needAlt   = (modifiers & 0x0001) != 0;
        bool needShift = (modifiers & 0x0004) != 0;
        bool needWin   = (modifiers & 0x0008) != 0;

        bool ctrlDown = IsAnyModifierDown(NativeMethods.VK_LCONTROL, NativeMethods.VK_RCONTROL, 0x11);
        bool altDown = IsAnyModifierDown(0xA4, 0xA5, 0x12);
        bool shiftDown = IsAnyModifierDown(0xA0, 0xA1, 0x10);
        bool winDown = IsAnyModifierDown(0x5B, 0x5C);

        if (needCtrl  && !ctrlDown)  return false;
        if (needAlt   && !altDown)   return false;
        if (needShift && !shiftDown) return false;
        if (needWin   && !winDown)   return false;
        return true;
    }

    private bool IsAnyModifierDown(params uint[] keys) => keys.Any(_pressedModifierKeys.Contains);

    private void UpdateModifierState(uint key, bool isDown)
    {
        bool isModifier = key is NativeMethods.VK_LCONTROL or NativeMethods.VK_RCONTROL or 0x11 or
            0xA0 or 0xA1 or 0x10 or 0xA4 or 0xA5 or 0x12 or 0x5B or 0x5C;
        if (!isModifier)
            return;

        if (isDown)
            _pressedModifierKeys.Add(key);
        else
            _pressedModifierKeys.Remove(key);
    }

    private static bool IsKeyDown(int key) =>
        (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;

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
