using BetterWinTab.Interop;
using static BetterWinTab.Interop.NativeMethods;

namespace BetterWinTab.Services;

/// <summary>
/// Manages DWM (Desktop Window Manager) thumbnail registrations for live window previews.
/// DWM thumbnails render a real-time mirror of another window's content with zero CPU cost.
/// </summary>
public class ThumbnailService : IDisposable
{
    private readonly Dictionary<IntPtr, IntPtr> _thumbnails = new(); // sourceHwnd -> thumbnailId
    private bool _disposed;

    /// <summary>
    /// Registers a DWM thumbnail that projects <paramref name="sourceWindow"/> into <paramref name="destinationWindow"/>.
    /// </summary>
    /// <returns>The thumbnail handle, or IntPtr.Zero on failure.</returns>
    public IntPtr RegisterThumbnail(IntPtr destinationWindow, IntPtr sourceWindow)
    {
        // Unregister existing thumbnail for this source if any
        UnregisterThumbnail(sourceWindow);

        int hr = NativeMethods.DwmRegisterThumbnail(destinationWindow, sourceWindow, out IntPtr thumbnailId);

        if (hr != 0)
            return IntPtr.Zero;

        _thumbnails[sourceWindow] = thumbnailId;
        return thumbnailId;
    }

    /// <summary>
    /// Updates the thumbnail's destination rectangle and visibility.
    /// </summary>
    public bool UpdateThumbnail(IntPtr thumbnailId, RECT destinationRect, bool visible = true, byte opacity = 255)
    {
        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_RECTDESTINATION
                    | DWM_TNP_VISIBLE
                    | DWM_TNP_OPACITY
                    | DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = destinationRect,
            fVisible = visible,
            opacity = opacity,
            fSourceClientAreaOnly = true
        };

        int hr = NativeMethods.DwmUpdateThumbnailProperties(thumbnailId, ref props);
        return hr == 0;
    }

    /// <summary>
    /// Updates the thumbnail with both destination AND source clipping rects.
    /// Used to prevent thumbnails from overflowing their viewport boundary:
    /// the source rect is proportionally sliced to match the visible portion of the destination.
    /// </summary>
    public bool UpdateThumbnailWithSourceClip(IntPtr thumbnailId, RECT destinationRect, RECT sourceRect, byte opacity = 255)
    {
        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_RECTDESTINATION
                    | DWM_TNP_RECTSOURCE
                    | DWM_TNP_VISIBLE
                    | DWM_TNP_OPACITY
                    | DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = destinationRect,
            rcSource = sourceRect,
            fVisible = true,
            opacity = opacity,
            fSourceClientAreaOnly = true
        };

        int hr = NativeMethods.DwmUpdateThumbnailProperties(thumbnailId, ref props);
        return hr == 0;
    }

    /// <summary>
    /// Gets the source window's size via the DWM thumbnail API.
    /// </summary>
    public (int width, int height) GetSourceSize(IntPtr thumbnailId)
    {
        int hr = NativeMethods.DwmQueryThumbnailSourceSize(thumbnailId, out SIZE size);
        if (hr != 0)
            return (0, 0);

        return (size.Width, size.Height);
    }

    /// <summary>
    /// Unregisters a specific thumbnail by source window handle.
    /// </summary>
    public void UnregisterThumbnail(IntPtr sourceWindow)
    {
        if (_thumbnails.TryGetValue(sourceWindow, out var thumbId))
        {
            NativeMethods.DwmUnregisterThumbnail(thumbId);
            _thumbnails.Remove(sourceWindow);
        }
    }

    /// <summary>
    /// Unregisters a thumbnail by its ID.
    /// </summary>
    public void UnregisterThumbnailById(IntPtr thumbnailId)
    {
        var entry = _thumbnails.FirstOrDefault(kv => kv.Value == thumbnailId);
        if (entry.Key != IntPtr.Zero)
        {
            NativeMethods.DwmUnregisterThumbnail(thumbnailId);
            _thumbnails.Remove(entry.Key);
        }
    }

    /// <summary>
    /// Returns the active thumbnail handle for <paramref name="sourceWindow"/>, or IntPtr.Zero if none.
    /// Used by position-only updates during scroll.
    /// </summary>
    public IntPtr TryGetThumbnailId(IntPtr sourceWindow)
    {
        return _thumbnails.TryGetValue(sourceWindow, out var thumbId) ? thumbId : IntPtr.Zero;
    }

    /// <summary>
    /// Unregisters all active thumbnails.
    /// </summary>
    public void UnregisterAll()
    {
        foreach (var thumbId in _thumbnails.Values)
        {
            NativeMethods.DwmUnregisterThumbnail(thumbId);
        }
        _thumbnails.Clear();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            UnregisterAll();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
