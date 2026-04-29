using System.Runtime.InteropServices;

namespace BetterWinTab.Interop;

/// <summary>
/// Manual Win32 P/Invoke declarations for BetterWinTab.
/// Using LibraryImport (.NET 7+ source-generated marshalling) where possible,
/// DllImport for delegates/callbacks.
/// </summary>
public static partial class NativeMethods
{
    // ═══════════════════════════════════════════════════════════════
    // Constants
    // ═══════════════════════════════════════════════════════════════

    public const int GWL_EXSTYLE = -20;
    public const int GWL_STYLE = -16;

    // Extended Window Styles
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_APPWINDOW = 0x00040000;
    public const uint WS_EX_NOACTIVATE = 0x08000000;
    public const uint WS_EX_TRANSPARENT = 0x00000020;
    public const uint WS_EX_LAYERED = 0x00080000;
    public const uint WS_EX_TOPMOST = 0x00000008;

    // Window Styles
    public const uint WS_VISIBLE = 0x10000000;

    // Show Window Commands
    public const int SW_HIDE = 0;
    public const int SW_SHOW = 5;
    public const int SW_RESTORE = 9;
    public const int SW_MINIMIZE = 6;

    // Window Styles (for stripping borders)
    public const uint WS_CAPTION = 0x00C00000;
    public const uint WS_THICKFRAME = 0x00040000;
    public const uint WS_BORDER = 0x00800000;
    public const uint WS_SYSMENU = 0x00080000;

    // SetWindowPos flags
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

    // GetWindow Commands
    public const uint GW_OWNER = 4;

    // GetAncestor flags
    public const uint GA_ROOT = 2;
    public const uint GA_ROOTOWNER = 3;

    // DWM Thumbnail Properties Flags
    public const uint DWM_TNP_RECTDESTINATION = 0x00000001;
    public const uint DWM_TNP_RECTSOURCE = 0x00000002;
    public const uint DWM_TNP_OPACITY = 0x00000004;
    public const uint DWM_TNP_VISIBLE = 0x00000008;
    public const uint DWM_TNP_SOURCECLIENTAREAONLY = 0x00000010;

    // Hotkey Modifiers
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    // Messages
    public const int WM_HOTKEY = 0x0312;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;

    // Keyboard Hook
    public const int WH_KEYBOARD_LL = 13;
    public const int VK_TAB = 0x09;
    public const int VK_LCONTROL = 0xA2;
    public const int VK_RCONTROL = 0xA3;
    public const int VK_CONTROL = 0x11;

    // ═══════════════════════════════════════════════════════════════
    // Structs
    // ═══════════════════════════════════════════════════════════════

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DWM_THUMBNAIL_PROPERTIES
    {
        public uint dwFlags;
        public RECT rcDestination;
        public RECT rcSource;
        public byte opacity;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fVisible;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fSourceClientAreaOnly;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // ═══════════════════════════════════════════════════════════════
    // Delegates
    // ═══════════════════════════════════════════════════════════════

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    // ═══════════════════════════════════════════════════════════════
    // User32.dll
    // ═══════════════════════════════════════════════════════════════

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, char[] lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetClassName(IntPtr hWnd, char[] lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr hWnd);

    // ═══════════════════════════════════════════════════════════════
    // Low-Level Keyboard Hook
    // ═══════════════════════════════════════════════════════════════

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    // ═══════════════════════════════════════════════════════════════
    // Focus Helpers (AttachThreadInput trick for reliable foreground)
    // ═══════════════════════════════════════════════════════════════

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    // ═══════════════════════════════════════════════════════════════
    // Shell32.dll — File / App Icon Extraction
    // ═══════════════════════════════════════════════════════════════

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    public const uint SHGFI_ICON           = 0x000000100;
    public const uint SHGFI_SMALLICON      = 0x000000001;
    public const uint SHGFI_LARGEICON      = 0x000000000;
    public const uint SHGFI_LINKOVERLAY    = 0x000008000; // add shortcut overlay
    public const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    // ═══════════════════════════════════════════════════════════════
    // Dwmapi.dll — Desktop Window Manager (live thumbnails)
    // ═══════════════════════════════════════════════════════════════

    // ── Window messages ──────────────────────────────────────────────
    public const uint WM_CLOSE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    // ── DWM Thumbnails ──────────────────────────────────────────────
    [DllImport("dwmapi.dll")]
    public static extern int DwmRegisterThumbnail(IntPtr hwndDestination, IntPtr hwndSource, out IntPtr phThumbnailId);

    [DllImport("dwmapi.dll")]
    public static extern int DwmUnregisterThumbnail(IntPtr hThumbnailId);

    [DllImport("dwmapi.dll")]
    public static extern int DwmUpdateThumbnailProperties(IntPtr hThumbnailId, ref DWM_THUMBNAIL_PROPERTIES ptnProperties);

    [DllImport("dwmapi.dll")]
    public static extern int DwmQueryThumbnailSourceSize(IntPtr hThumbnail, out SIZE pSize);

    // ═══════════════════════════════════════════════════════════════
    // Window Subclassing (comctl32)
    // ═══════════════════════════════════════════════════════════════

    public delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    public static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
}

// ═══════════════════════════════════════════════════════════════
// Virtual Desktop COM Interface (Windows 10 1803+ / Windows 11)
// ═══════════════════════════════════════════════════════════════

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
public interface IVirtualDesktopManager
{
    [PreserveSig]
    int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, out bool onCurrentDesktop);

    [PreserveSig]
    int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);

    [PreserveSig]
    int MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
}

[ComImport]
[Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")]
public class VirtualDesktopManagerClass { }
