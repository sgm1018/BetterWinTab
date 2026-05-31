using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using BetterWinTab.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;

namespace BetterWinTab.Services;

/// <summary>
/// Monitors the Windows clipboard using AddClipboardFormatListener and maintains
/// a history of the last N clipboard entries (text only for v1; images tracked but not stored).
/// </summary>
public class ClipboardService : IDisposable
{
    // ── Win32 Clipboard APIs ────────────────────────────────────
    private const int WM_CLIPBOARDUPDATE = 0x031D;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr hMem);

    private const uint CF_UNICODETEXT = 13;
    private const uint CF_BITMAP = 2;
    private const uint CF_DIB = 8;
    private const uint GMEM_MOVEABLE = 0x0002;

    // ── State ─────────────────────────────────────────────────
    private IntPtr _hwnd;
    private bool _listening;
    private bool _disposed;
    private int _maxItems;
    private bool _selfCopying; // prevent re-capturing our own copy-to-clipboard action
    private DispatcherQueue? _dispatcherQueue;
    private DateTime _lastImageCaptureTime = DateTime.MinValue;

    /// <summary>Observable clipboard history, newest first.</summary>
    public ObservableCollection<ClipboardItem> History { get; } = new();

    /// <summary>Fired when a new item is captured.</summary>
    public event Action? ClipboardChanged;

    /// <summary>
    /// Starts listening for clipboard changes on the given HWND.
    /// Must be called from the UI thread.
    /// </summary>
    public void Start(IntPtr hwnd, int maxItems = 50, DispatcherQueue? dispatcherQueue = null)
    {
        if (_listening) return;
        _hwnd = hwnd;
        _maxItems = maxItems;
        _dispatcherQueue = dispatcherQueue;
        _listening = AddClipboardFormatListener(hwnd);
    }

    public void Stop()
    {
        if (!_listening || _hwnd == IntPtr.Zero) return;
        RemoveClipboardFormatListener(_hwnd);
        _listening = false;
    }

    /// <summary>
    /// Process a Win32 message. Call this from a subclassed WndProc or message loop.
    /// Returns true if the message was handled (WM_CLIPBOARDUPDATE).
    /// </summary>
    public bool ProcessMessage(uint msg)
    {
        if (msg == WM_CLIPBOARDUPDATE && !_selfCopying)
        {
            CaptureClipboard();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Captures the current clipboard content into history.
    /// </summary>
    private void CaptureClipboard()
    {
        try
        {
            if (IsClipboardFormatAvailable(CF_UNICODETEXT))
            {
                if (OpenClipboard(IntPtr.Zero))
                {
                    try
                    {
                        var hData = GetClipboardData(CF_UNICODETEXT);
                        if (hData != IntPtr.Zero)
                        {
                            var ptr = GlobalLock(hData);
                            if (ptr != IntPtr.Zero)
                            {
                                try
                                {
                                    var text = Marshal.PtrToStringUni(ptr);
                                    if (!string.IsNullOrEmpty(text))
                                    {
                                        // Deduplicate: if this text already exists anywhere
                                        // in history, move it to the top instead of inserting
                                        // a duplicate entry.
                                        var existing = History.FirstOrDefault(h => !h.IsImage && h.Text == text);
                                        if (existing != null)
                                        {
                                            int idx = History.IndexOf(existing);
                                            if (idx == 0) return;  // already at top
                                            History.Move(idx, 0);
                                        }
                                        else
                                        {
                                            var item = new ClipboardItem { Text = text, IsImage = false };
                                            History.Insert(0, item);
                                            TrimHistory();
                                        }
                                        ClipboardChanged?.Invoke();
                                    }
                                }
                                finally
                                {
                                    GlobalUnlock(hData);
                                }
                            }
                        }
                    }
                    finally
                    {
                        CloseClipboard();
                    }
                }
            }
            else if (IsClipboardFormatAvailable(CF_BITMAP) || IsClipboardFormatAvailable(CF_DIB))
            {
                // Debounce rapid duplicate image messages (some apps fire
                // WM_CLIPBOARDUPDATE multiple times for a single copy)
                if ((DateTime.UtcNow - _lastImageCaptureTime).TotalMilliseconds < 500)
                    return;
                _lastImageCaptureTime = DateTime.UtcNow;

                var item = new ClipboardItem { IsImage = true };
                History.Insert(0, item);
                TrimHistory();
                ClipboardChanged?.Invoke();

                // Load the actual bitmap preview on the UI thread via WinRT clipboard API
                _dispatcherQueue?.TryEnqueue(DispatcherQueuePriority.Low,
                    async () => await CaptureImageAsync(item));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ClipboardService.CaptureClipboard: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads the clipboard image into the given item via the WinRT Clipboard API.
    /// Must be called on the UI thread (DispatcherQueue).
    /// </summary>
    private static async Task CaptureImageAsync(ClipboardItem item)
    {
        try
        {
            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Bitmap))
            {
                var streamRef = await content.GetBitmapAsync();
                using var stream = await streamRef.OpenReadAsync();
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(stream);
                item.ImageSource = bitmap;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ClipboardService.CaptureImageAsync: {ex.Message}");
        }
    }

    /// <summary>
    /// Copies a history item back to the clipboard.
    /// </summary>
    public bool CopyToClipboard(ClipboardItem item)
    {
        if (item.IsImage || string.IsNullOrEmpty(item.Text))
            return false;

        return CopyTextToClipboard(item.Text);
    }

    public bool CopyTextToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        _selfCopying = true;
        try
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    EmptyClipboard();
                    var bytes = (text.Length + 1) * 2; // UTF-16 + null terminator
                    var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
                    if (hGlobal != IntPtr.Zero)
                    {
                        var ptr = GlobalLock(hGlobal);
                        if (ptr != IntPtr.Zero)
                        {
                            Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
                            Marshal.WriteInt16(ptr, text.Length * 2, 0); // null terminator
                            GlobalUnlock(hGlobal);
                        }
                        SetClipboardData(CF_UNICODETEXT, hGlobal);
                        // Don't free hGlobal — clipboard owns it now
                    }
                    return true;
                }
                finally
                {
                    CloseClipboard();
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            // Delay resetting _selfCopying so that any WM_CLIPBOARDUPDATE
            // message posted (not sent) by CloseClipboard is still suppressed
            // when the message pump processes it.
            if (_dispatcherQueue != null)
            {
                _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () => _selfCopying = false);
            }
            else
            {
                _selfCopying = false;
            }
        }
    }

    /// <summary>
    /// Removes a specific item from history.
    /// </summary>
    public void RemoveItem(ClipboardItem item)
    {
        History.Remove(item);
    }

    /// <summary>
    /// Clears all clipboard history.
    /// </summary>
    public void ClearHistory()
    {
        History.Clear();
    }

    private void TrimHistory()
    {
        while (History.Count > _maxItems)
            History.RemoveAt(History.Count - 1);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_listening && _hwnd != IntPtr.Zero)
        {
            RemoveClipboardFormatListener(_hwnd);
            _listening = false;
        }
    }
}
