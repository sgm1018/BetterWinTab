using BetterWinTab.Interop;

namespace BetterWinTab.Services;

public static class KeyboardHelper
{
    public static bool IsTypableKey(Windows.System.VirtualKey key)
    {
        int k = (int)key;
        return (k >= (int)Windows.System.VirtualKey.A && k <= (int)Windows.System.VirtualKey.Z)
            || (k >= (int)Windows.System.VirtualKey.Number0 && k <= (int)Windows.System.VirtualKey.Number9)
            || (k >= (int)Windows.System.VirtualKey.NumberPad0 && k <= (int)Windows.System.VirtualKey.NumberPad9);
    }

    public static char VirtualKeyToChar(Windows.System.VirtualKey key)
    {
        int k = (int)key;

        if (k >= (int)Windows.System.VirtualKey.A && k <= (int)Windows.System.VirtualKey.Z)
        {
            var capsState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.CapitalLock);
            var shiftState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
            bool capsOn = (capsState & Windows.UI.Core.CoreVirtualKeyStates.Locked) != 0;
            bool shiftDown = (shiftState & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
            char lower = (char)('a' + k - (int)Windows.System.VirtualKey.A);
            return (capsOn ^ shiftDown) ? (char)(lower - 32) : lower;
        }

        if (k >= (int)Windows.System.VirtualKey.Number0 && k <= (int)Windows.System.VirtualKey.Number9)
            return (char)('0' + k - (int)Windows.System.VirtualKey.Number0);

        if (k >= (int)Windows.System.VirtualKey.NumberPad0 && k <= (int)Windows.System.VirtualKey.NumberPad9)
            return (char)('0' + k - (int)Windows.System.VirtualKey.NumberPad0);

        return '\0';
    }

    public static uint GetCurrentModifiers()
    {
        uint mods = 0;
        bool ctrlDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LCONTROL) & 0x8000) != 0
                      || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RCONTROL) & 0x8000) != 0;
        bool altDown = (NativeMethods.GetAsyncKeyState(0x12) & 0x8000) != 0;
        bool shiftDown = (NativeMethods.GetAsyncKeyState(0x10) & 0x8000) != 0;
        bool winDown = (NativeMethods.GetAsyncKeyState(0x5B) & 0x8000) != 0
                      || (NativeMethods.GetAsyncKeyState(0x5C) & 0x8000) != 0;
        if (ctrlDown) mods |= 0x0002;
        if (altDown) mods |= 0x0001;
        if (shiftDown) mods |= 0x0004;
        if (winDown) mods |= 0x0008;
        return mods;
    }

    public static string FormatHotkey(uint modifiers, uint vKey)
    {
        var parts = new List<string>();
        if ((modifiers & 0x0002) != 0) parts.Add("Ctrl");
        if ((modifiers & 0x0001) != 0) parts.Add("Alt");
        if ((modifiers & 0x0004) != 0) parts.Add("Shift");
        if ((modifiers & 0x0008) != 0) parts.Add("Win");
        parts.Add(vKey switch
        {
            0x09 => "Tab",
            0x20 => "Space",
            0x1B => "Esc",
            0x0D => "Enter",
            0x2E => "Del",
            0xC0 => "`",
            0xBD => "-",
            0xBB => "=",
            >= 0x41 and <= 0x5A => ((char)vKey).ToString(),
            >= 0x30 and <= 0x39 => ((char)vKey).ToString(),
            >= 0x70 and <= 0x7B => $"F{vKey - 0x6F}",
            >= 0x60 and <= 0x69 => $"Num{vKey - 0x60}",
            _ => $"0x{vKey:X2}"
        });
        return string.Join(" + ", parts);
    }
}
