using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using BetterWinTab.Interop;
using BetterWinTab.Models;
using BetterWinTab.Services;
using WinRT.Interop;

namespace BetterWinTab.Views;

/// <summary>
/// Main overlay page with folder sidebar and window grid.
/// Handles keyboard navigation, DWM thumbnail rendering, and drag-and-drop folder reordering.
/// </summary>
public sealed partial class MainPage : Page
{
    
    public MainViewModel ViewModel { get; }
    private readonly ThumbnailService _thumbnailService = new();
    private bool _thumbnailsPending;
    private int _thumbGeneration;   // incremented on every folder switch / refresh
    private int _draggedFolderOldIndex = -1;
    private ScrollViewer? _windowGridScrollViewer;

    // ── Delta-based DWM thumbnail scroll tracking ──
    // DWM thumbnails are positioned at absolute screen pixels by the compositor.
    // During DirectManipulation (smooth scroll), TransformToVisual returns stale
    // layout positions, so thumbnails appear "fixed". Instead we cache the pixel
    // rect of each thumbnail at registration time (when scroll is idle), record
    // the VerticalOffset, and during scroll simply shift every rect by the delta.
    private readonly Dictionary<IntPtr, NativeMethods.RECT> _registeredThumbRects = new();
    private double _thumbRegistrationScrollY;

    // ── Context-menu target tracking ──
    // MenuFlyoutItem inside a ContextFlyout doesn't reliably inherit DataContext
    // from its parent Border in WinUI 3 (flyouts live in a separate popup layer).
    // We capture the target explicitly on RightTapped so all context handlers work.
    private WindowItemViewModel? _contextMenuTarget;

    // ── Window subclass for clipboard messages ──
    private NativeMethods.SUBCLASSPROC? _subclassProc;
    private static readonly UIntPtr SUBCLASS_ID = (UIntPtr)1001;

    private bool IsAnyModalOpen =>
        ViewModel.Onboarding.IsOnboardingVisible ||
        ViewModel.Settings.IsSettingsPanelVisible ||
        ViewModel.IsAddFolderPanelVisible ||
        ViewModel.IsEditFolderPanelVisible;

    public MainPage()
    {
        ViewModel = new MainViewModel();
        this.InitializeComponent();
        this.Loaded += MainPage_Loaded;
        ViewModel.Windows.CollectionChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                UpdateWindowGridColumnCountFromLayout();
            });
        };

        // ── Bulletproof keyboard interception ──
        // In WinUI 3 Desktop, the most reliable way to intercept keys globally
        // before controls like TextBox or GridView swallow them is via the DispatcherQueue.
        this.Loaded += (_, _) =>
        {
            this.Focus(FocusState.Programmatic);
            
            // Hook into the XamlRoot's keyboard events
            if (this.XamlRoot != null)
            {
                this.XamlRoot.Content.PreviewKeyDown += OnGlobalPreviewKeyDown;
            }
        };
        this.Unloaded += (_, _) =>
        {
            if (this.XamlRoot != null)
            {
                this.XamlRoot.Content.PreviewKeyDown -= OnGlobalPreviewKeyDown;
            }
            if (_windowGridScrollViewer != null)
                _windowGridScrollViewer.ViewChanged -= WindowGridScrollViewer_ViewChanged;
        };

        // Listen for folder changes to refresh thumbnails
        ViewModel.WindowsRefreshed += OnWindowsRefreshed;

        ViewModel.Settings.AppearanceChanged += OnAppearanceChanged;
        ViewModel.Settings.AppearancePreviewChanged += () => ApplyPreviewAppearance();
        ViewModel.Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.IsSettingsPanelVisible))
            {
                bool isModalOpen = IsAnyModalOpen;
                if (isModalOpen)
                {
                    _thumbnailService.UnregisterAll();
                    _registeredThumbRects.Clear();
                    if (ViewModel.Settings.IsSettingsPanelVisible)
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (SettingsNav?.Items.Count > 0)
                                SettingsNav.SelectedIndex = 0;
                        });
                    }
                }
                else
                {
                    ScheduleThumbnailRegistration();
                }
            }
        };
        ViewModel.Onboarding.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OnboardingViewModel.IsOnboardingVisible))
            {
                if (IsAnyModalOpen)
                {
                    _thumbnailService.UnregisterAll();
                    _registeredThumbRects.Clear();
                }
                else
                {
                    ScheduleThumbnailRegistration();
                }
            }
        };

        // Hide DWM thumbnails when any modal is open, as they render on top of XAML
        // Also reset settings nav to first tab when the settings panel opens
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsAddFolderPanelVisible) ||
                e.PropertyName == nameof(MainViewModel.IsEditFolderPanelVisible))
            {
                bool isModalOpen = IsAnyModalOpen;
                
                if (isModalOpen)
                {
                    _thumbnailService.UnregisterAll();
                    _registeredThumbRects.Clear();
                }
                else
                {
                    ScheduleThumbnailRegistration();
                }
            }
        };
    }

    private void OnAppearanceChanged()
    {
        ThemeApplier.Apply(ViewModel.GetAppearanceSettings());
    }

    public void ApplyPreviewAppearance()
    {
        ThemeApplier.Apply(new BetterWinTab.Models.AppearanceSettings
        {
            AccentColor        = ViewModel.Settings.SettingsAccentColor,
            AccentDimColor     = ViewModel.Settings.SettingsAccentDimColor,
            AccentSubtleColor  = ViewModel.Settings.SettingsAccentSubtleColor,
            BackgroundColor    = ViewModel.Settings.SettingsBackgroundColor,
            SurfaceColor       = ViewModel.Settings.SettingsSurfaceColor,
            CardColor          = ViewModel.Settings.SettingsCardColor,
            BorderColor        = ViewModel.Settings.SettingsBorderColor,
            TextPrimaryColor   = ViewModel.Settings.SettingsTextPrimaryColor,
            TextSecondaryColor = ViewModel.Settings.SettingsTextSecondaryColor,
            TextMutedColor     = ViewModel.Settings.SettingsTextMutedColor,
            DangerColor        = ViewModel.Settings.SettingsDangerColor,
            FolderHoverColor   = ViewModel.Settings.SettingsFolderHoverColor,
            FolderSelectedColor = ViewModel.Settings.SettingsFolderSelectedColor,
            WindowHoverBorderColor = ViewModel.Settings.SettingsWindowHoverBorderColor,
            WindowHoverBackgroundColor = ViewModel.Settings.SettingsWindowHoverBackgroundColor,
        });
    }

    public void ApplyAppearanceFromSettings()
    {
        ThemeApplier.Apply(ViewModel.GetAppearanceSettings());
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        // Apply saved appearance settings on startup
        ApplyAppearanceFromSettings();
        // Provide the overlay HWND so VirtualDesktopService can determine current virtual desktop
        ViewModel.SetOverlayHwnd(App.Current.Hwnd);

        // Start clipboard monitoring service (pass DispatcherQueue for async image loading on UI thread)
        ViewModel.StartClipboardService(App.Current.Hwnd, DispatcherQueue);

        // Install window subclass to intercept WM_CLIPBOARDUPDATE
        _subclassProc = new NativeMethods.SUBCLASSPROC(SubclassWndProc);
        NativeMethods.SetWindowSubclass(App.Current.Hwnd, _subclassProc, SUBCLASS_ID, UIntPtr.Zero);

        // Hook into the GridView's internal ScrollViewer so we can re-register DWM thumbnails
        // whenever the user scrolls. DWM thumbnails are at absolute pixel positions and don't
        // move automatically when XAML content scrolls.
        
        Action bindScrollViewer = () =>
        {
            if (_windowGridScrollViewer != null) return;
            _windowGridScrollViewer = FindChildByType<ScrollViewer>(WindowGrid);
            if (_windowGridScrollViewer != null)
            {
                _windowGridScrollViewer.ViewChanged += WindowGridScrollViewer_ViewChanged;
                // Synchronous per-frame callback: keeps DWM thumbnails glued to their
                // XAML cards during every scroll tick (ViewChanged alone fires too late).
                _windowGridScrollViewer.RegisterPropertyChangedCallback(
                    ScrollViewer.VerticalOffsetProperty,
                    (_, _) => UpdateDwmThumbnailPositions());
            }
        };

        if (WindowGrid.IsLoaded)
        {
            bindScrollViewer();
        }
        else
        {
            WindowGrid.Loaded += (_, _) => bindScrollViewer();
        }
    }

    /// <summary>
    /// Called when the overlay becomes visible (hotkey pressed).
    /// </summary>
    public void OnOverlayShown()
    {
        // Clear any leftover search from last session
        ViewModel.ClearSearch();
        ViewModel.RefreshWindows();

        // Immediate focus attempt
        this.Focus(FocusState.Programmatic);

        // Normal priority: after basic layout
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            this.Focus(FocusState.Programmatic);
        });

        // Low priority: after all layout + Win32 focus has landed
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            ForceFocusToPage();
        });
    }

    /// <summary>
    /// Forces both Win32 and XAML focus to this page.
    /// Called from App.ShowOverlay after Win32 foreground is established.
    /// </summary>
    public void ForceFocusToPage()
    {
        var hwnd = App.Current.Hwnd;
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.SetForegroundWindow(hwnd);
            NativeMethods.SetFocus(hwnd);
        }
        this.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Called whenever the ViewModel refreshes the window list (folder change, F5, etc.).
    /// Schedules DWM thumbnail re-registration.
    /// </summary>
    private void OnWindowsRefreshed()
    {
        // Immediately clear stale DWM thumbnails so old previews don't float
        // while the new registration runs.
        _thumbnailService.UnregisterAll();
        _registeredThumbRects.Clear();

        if (IsAnyModalOpen)
            return;

        // Cancel any in-flight retry timer from a previous generation
        _thumbnailRetryTimer?.Stop();
        _thumbnailRetryTimer = null;

        // Bump generation so any Low-priority callbacks queued BEFORE this point
        // (e.g. from GridView SizeChanged during the old folder) are recognised
        // as stale and skipped — they would otherwise register thumbnails at
        // intermediate / wrong layout positions.
        _thumbGeneration++;

        // Force-reset the pending flag so the new schedule always goes through,
        // even if a previous ScheduleThumbnailRegistration is still queued.
        _thumbnailsPending = false;

        UpdateWindowGridColumnCountFromLayout();
        ScheduleThumbnailRegistration();

        // Ensure the selected item (first search result / first window) is visible
        // and has the correct visual highlight. Deferred because the GridView needs
        // to materialise containers before we can scroll or set visual states.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (ViewModel.SelectedWindow != null)
            {
                ClearAllPointerOverStates();
                ScrollSelectedWindowIntoView();
            }
        });
    }

    /// <summary>
    /// Called when the overlay is about to hide.
    /// Unregisters all DWM thumbnails to free resources.
    /// </summary>
    public void OnOverlayHidden()
    {
        // Clear search so the overlay starts fresh next time
        ViewModel.ClearSearch();
        _thumbnailService.UnregisterAll();
        _registeredThumbRects.Clear();
    }

    private DispatcherTimer? _scrollDebounceTimer;

    /// <summary>
    /// Fired when the WindowGrid scrolls. Repositions DWM thumbnails to follow the XAML cards.
    /// </summary>
    private void WindowGridScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        // Immediately reposition existing thumbnails (cheap DWM call, no re-register).
        // This keeps the live previews glued to their cards during scroll.
        UpdateDwmThumbnailPositions();

        if (_scrollDebounceTimer == null)
        {
            _scrollDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _scrollDebounceTimer.Tick += (s, args) =>
            {
                _scrollDebounceTimer.Stop();
                ScheduleThumbnailRegistration();
            };
        }
        
        _scrollDebounceTimer.Stop();
        _scrollDebounceTimer.Start();
    }

    /// <summary>
    /// Recursively walks the visual tree to find the first child of type T.
    /// </summary>
    private static T? FindChildByType<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var found = FindChildByType<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// Lightweight: repositions existing DWM thumbnails without unregister/register.
    /// Uses a delta-based approach: computes the scroll offset change since registration
    /// and shifts every cached pixel rect by that delta. Thumbnails that overflow the
    /// viewport boundary are clipped via DWM source-rect to prevent bleeding over the
    /// search bar / status bar.
    /// </summary>
    private void UpdateDwmThumbnailPositions()
    {
        if (IsAnyModalOpen)
        {
            _thumbnailService.UnregisterAll();
            _registeredThumbRects.Clear();
            return;
        }

        var hwnd = App.Current.Hwnd;
        if (hwnd == IntPtr.Zero || _windowGridScrollViewer == null) return;

        var scale = this.XamlRoot?.RasterizationScale ?? 1.0;
        int windowCount = ViewModel.Windows.Count;

        // Pixel delta between the current scroll offset and the offset at registration time
        double scrollDeltaY = _windowGridScrollViewer.VerticalOffset - _thumbRegistrationScrollY;
        int deltaPixelsY = (int)Math.Round(scrollDeltaY * scale);

        // Viewport bounds in physical pixels
        var svTransform = _windowGridScrollViewer.TransformToVisual(null);
        var svOrigin = svTransform.TransformPoint(new Windows.Foundation.Point(0, 0));
        var viewport = new NativeMethods.RECT
        {
            Left   = (int)(svOrigin.X * scale),
            Top    = (int)(svOrigin.Y * scale),
            Right  = (int)((svOrigin.X + _windowGridScrollViewer.ViewportWidth) * scale),
            Bottom = (int)((svOrigin.Y + _windowGridScrollViewer.ViewportHeight) * scale)
        };

        for (int i = 0; i < windowCount; i++)
        {
            var sourceHwnd = ViewModel.Windows[i].Model.Handle;
            var thumbId = _thumbnailService.TryGetThumbnailId(sourceHwnd);
            if (thumbId == IntPtr.Zero) continue;

            if (!_registeredThumbRects.TryGetValue(sourceHwnd, out var origRect))
            {
                _thumbnailService.UpdateThumbnail(thumbId, default, visible: false);
                continue;
            }

            var adjusted = new NativeMethods.RECT
            {
                Left   = origRect.Left,
                Top    = origRect.Top - deltaPixelsY,
                Right  = origRect.Right,
                Bottom = origRect.Bottom - deltaPixelsY
            };

            UpdateThumbnailWithClipping(thumbId, adjusted, viewport);
        }
    }

    /// <summary>
    /// Updates a DWM thumbnail applying viewport clipping.
    /// When a thumbnail partially overlaps the viewport edge, this method clips both
    /// the destination rect and, proportionally, the source rect so the preview image
    /// is sliced rather than scaled — preserving correct pixel mapping and preventing
    /// thumbnails from bleeding over the app header / footer bars.
    /// </summary>
    private void UpdateThumbnailWithClipping(IntPtr thumbId, NativeMethods.RECT dest, NativeMethods.RECT viewport)
    {
        int dstW = dest.Right  - dest.Left;
        int dstH = dest.Bottom - dest.Top;
        if (dstW <= 0 || dstH <= 0)
        {
            _thumbnailService.UpdateThumbnail(thumbId, default, visible: false);
            return;
        }

        // Intersect thumbnail with viewport
        int clL = Math.Max(dest.Left,   viewport.Left);
        int clT = Math.Max(dest.Top,    viewport.Top);
        int clR = Math.Min(dest.Right,  viewport.Right);
        int clB = Math.Min(dest.Bottom, viewport.Bottom);

        if (clR <= clL || clB <= clT)
        {
            // Completely outside viewport
            _thumbnailService.UpdateThumbnail(thumbId, default, visible: false);
            return;
        }

        if (clL == dest.Left && clT == dest.Top && clR == dest.Right && clB == dest.Bottom)
        {
            // Fully inside — no clipping needed
            _thumbnailService.UpdateThumbnail(thumbId, dest);
            return;
        }

        // Partially overlapping: compute proportional source slice
        var (srcW, srcH) = _thumbnailService.GetSourceSize(thumbId);
        if (srcW <= 0 || srcH <= 0)
        {
            // Source size unavailable — hide rather than show incorrectly
            _thumbnailService.UpdateThumbnail(thumbId, default, visible: false);
            return;
        }

        var clippedDst = new NativeMethods.RECT { Left = clL, Top = clT, Right = clR, Bottom = clB };
        var srcRect = new NativeMethods.RECT
        {
            Left   = (int)Math.Round((clL - dest.Left) * (double)srcW / dstW),
            Top    = (int)Math.Round((clT - dest.Top)  * (double)srcH / dstH),
            Right  = (int)Math.Round((clR - dest.Left) * (double)srcW / dstW),
            Bottom = (int)Math.Round((clB - dest.Top)  * (double)srcH / dstH)
        };

        _thumbnailService.UpdateThumbnailWithSourceClip(thumbId, clippedDst, srcRect);
    }

    /// <summary>
    /// Scrolls the WindowGrid viewport to ensure the currently selected window card is visible.
    /// Called after every keyboard navigation to prevent "phantom hover" confusion.
    /// </summary>
    private void ScrollSelectedWindowIntoView()
    {
        if (ViewModel.SelectedWindow == null) return;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            if (ViewModel.SelectedWindow != null)
                WindowGrid.ScrollIntoView(ViewModel.SelectedWindow, ScrollIntoViewAlignment.Default);
        });
    }

    /// <summary>
    /// Scrolls the clipboard ListView so the selected item is visible after keyboard navigation.
    /// </summary>
    private void ScrollSelectedClipboardItemIntoView()
    {
        if (ViewModel.SelectedClipboardItem == null) return;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            if (ViewModel.SelectedClipboardItem != null)
                ClipboardList.ScrollIntoView(ViewModel.SelectedClipboardItem, ScrollIntoViewAlignment.Default);
        });
    }

    /// <summary>
    /// Scrolls the app launcher ListView so the selected suggestion stays visible.
    /// </summary>
    private void ScrollSelectedLaunchItemIntoView()
    {
        if (ViewModel.SelectedLaunchItem == null) return;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            if (ViewModel.SelectedLaunchItem != null)
                LaunchList.ScrollIntoView(ViewModel.SelectedLaunchItem, ScrollIntoViewAlignment.Default);
        });
    }

    /// <summary>
    /// Schedules thumbnail registration once the GridView finishes layout.
    /// Coalesces multiple rapid calls into one registration pass.
    /// Retries automatically when containers are not yet materialized or sized.
    /// A generation counter ensures stale callbacks (queued before the latest
    /// folder switch) are silently skipped.
    /// </summary>
    private int _thumbnailRetryCount;
    private DispatcherTimer? _thumbnailRetryTimer;

    private void ScheduleThumbnailRegistration()
    {
        if (IsAnyModalOpen)
        {
            _thumbnailsPending = false;
            _thumbnailService.UnregisterAll();
            _registeredThumbRects.Clear();
            return;
        }

        if (_thumbnailsPending) return;
        _thumbnailsPending = true;
        _thumbnailRetryCount = 0;

        int gen = _thumbGeneration;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _thumbnailsPending = false;

            // A newer OnWindowsRefreshed has superseded us — bail out.
            if (gen != _thumbGeneration) return;

            RetryRegisterDwmThumbnails(gen);
        });
    }

    /// <summary>
    /// Attempts RegisterDwmThumbnails and retries up to 5 times with a short
    /// timer delay when not all visible containers have been sized yet.
    /// Bails out if the generation has changed (folder switched underneath us).
    /// </summary>
    private void RetryRegisterDwmThumbnails(int gen)
    {
        if (gen != _thumbGeneration || IsAnyModalOpen) return;

        int registered = RegisterDwmThumbnails();
        int expected = ViewModel.Windows.Count;

        // Retry when there are windows we couldn't register (containers not
        // materialized or ThumbnailHost not yet sized). Use a 50 ms timer
        // instead of immediate Low-priority queue so layout has time to settle.
        if (registered < expected && expected > 0 && _thumbnailRetryCount < 5)
        {
            _thumbnailRetryCount++;
            _thumbnailRetryTimer?.Stop();
            _thumbnailRetryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _thumbnailRetryTimer.Tick += (_, _) =>
            {
                _thumbnailRetryTimer!.Stop();
                _thumbnailRetryTimer = null;
                RetryRegisterDwmThumbnails(gen);
            };
            _thumbnailRetryTimer.Start();
        }
    }

    /// <summary>
    /// Registers (or reconciles) DWM thumbnails for each visible window card.
    /// Uses smart reconciliation instead of UnregisterAll+re-register to eliminate
    /// the brief flash (flicker) that occurred when every thumbnail disappeared
    /// momentarily during a full re-registration pass.
    /// — Thumbnails whose source window is still present are kept and repositioned.
    /// — Thumbnails for windows that have left the list are unregistered.
    /// — New windows get a freshly registered thumbnail.
    /// </summary>
    /// <returns>The number of thumbnails successfully registered/updated.</returns>
    private int RegisterDwmThumbnails()
    {
        if (IsAnyModalOpen)
            return 0;

        var hwnd = App.Current.Hwnd;
        if (hwnd == IntPtr.Zero) return 0;

        if (_windowGridScrollViewer == null)
        {
            _windowGridScrollViewer = FindChildByType<ScrollViewer>(WindowGrid);
            if (_windowGridScrollViewer != null)
            {
                _windowGridScrollViewer.ViewChanged += WindowGridScrollViewer_ViewChanged;
                _windowGridScrollViewer.RegisterPropertyChangedCallback(
                    ScrollViewer.VerticalOffsetProperty,
                    (_, _) => UpdateDwmThumbnailPositions());
            }
        }

        var scale = this.XamlRoot?.RasterizationScale ?? 1.0;
        int windowCount = ViewModel.Windows.Count;
        int registered = 0;

        // Snapshot scroll offset so delta-based repositioning stays in sync
        _thumbRegistrationScrollY = _windowGridScrollViewer?.VerticalOffset ?? 0;

        // Compute viewport bounds once for the whole pass
        NativeMethods.RECT? viewport = null;
        if (_windowGridScrollViewer != null)
        {
            var svT = _windowGridScrollViewer.TransformToVisual(null);
            var svO = svT.TransformPoint(new Windows.Foundation.Point(0, 0));
            viewport = new NativeMethods.RECT
            {
                Left   = (int)(svO.X * scale),
                Top    = (int)(svO.Y * scale),
                Right  = (int)((svO.X + _windowGridScrollViewer.ViewportWidth)  * scale),
                Bottom = (int)((svO.Y + _windowGridScrollViewer.ViewportHeight) * scale)
            };
        }

        // Track which handles we touch this pass so we can clean up stale ones
        var touchedHandles = new HashSet<IntPtr>();

        for (int i = 0; i < windowCount; i++)
        {
            var container = WindowGrid.ContainerFromIndex(i) as GridViewItem;
            if (container == null) continue;

            var thumbHost = FindChildByTag<Border>(container, "ThumbnailHost");
            if (thumbHost == null) continue;

            // If layout hasn't completed yet, the Border will have zero size.
            // Skip it — don't register a zero-size DWM thumbnail that would be
            // invisible. The retry loop will pick it up once layout settles.
            if (thumbHost.ActualWidth <= 0 || thumbHost.ActualHeight <= 0) continue;

            var sourceHwnd = ViewModel.Windows[i].Model.Handle;
            touchedHandles.Add(sourceHwnd);

            // Re-use an existing registration if available (avoids the brief hide/show flash)
            var thumbId = _thumbnailService.TryGetThumbnailId(sourceHwnd);
            if (thumbId == IntPtr.Zero)
            {
                thumbId = _thumbnailService.RegisterThumbnail(hwnd, sourceHwnd);
                if (thumbId == IntPtr.Zero) continue;
            }

            // Physical pixel coordinates for DWM
            var transform = thumbHost.TransformToVisual(null);
            var pos = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

            var destRect = new NativeMethods.RECT
            {
                Left   = (int)(pos.X * scale),
                Top    = (int)(pos.Y * scale),
                Right  = (int)((pos.X + thumbHost.ActualWidth)  * scale),
                Bottom = (int)((pos.Y + thumbHost.ActualHeight) * scale)
            };

            _registeredThumbRects[sourceHwnd] = destRect;

            if (viewport.HasValue)
                UpdateThumbnailWithClipping(thumbId, destRect, viewport.Value);
            else
                _thumbnailService.UpdateThumbnail(thumbId, destRect);

            registered++;
        }

        // Unregister thumbnails whose source windows are no longer in the list
        var staleHandles = _registeredThumbRects.Keys
            .Where(h => !touchedHandles.Contains(h))
            .ToList();
        foreach (var stale in staleHandles)
        {
            _thumbnailService.UnregisterThumbnail(stale);
            _registeredThumbRects.Remove(stale);
        }

        UpdateWindowGridColumnCountFromLayout();
        return registered;
    }

    /// <summary>
    /// Calculates the current number of visible columns from the laid out GridView items
    /// and pushes that value into the ViewModel so Up/Down navigation follows real rows.
    /// </summary>
    private void UpdateWindowGridColumnCountFromLayout()
    {
        if (WindowGrid == null || ViewModel.Windows.Count == 0)
        {
            ViewModel.SetWindowGridColumnCount(1);
            return;
        }

        int firstRowCount = 0;
        double? firstRowY = null;

        for (int i = 0; i < ViewModel.Windows.Count; i++)
        {
            var container = WindowGrid.ContainerFromIndex(i) as GridViewItem;
            if (container == null) continue;

            var p = container.TransformToVisual(WindowGrid).TransformPoint(new Windows.Foundation.Point(0, 0));

            if (!firstRowY.HasValue)
            {
                firstRowY = p.Y;
                firstRowCount = 1;
                continue;
            }

            if (Math.Abs(p.Y - firstRowY.Value) <= 1.0)
            {
                firstRowCount++;
            }
            else
            {
                break;
            }
        }

        if (firstRowCount <= 0)
        {
            if (WindowGrid.ItemsPanelRoot is ItemsWrapGrid panel && panel.ItemWidth > 0)
            {
                firstRowCount = Math.Max(1, (int)Math.Floor(WindowGrid.ActualWidth / panel.ItemWidth));
            }
            else
            {
                firstRowCount = 1;
            }
        }

        ViewModel.SetWindowGridColumnCount(firstRowCount);
    }

    /// <summary>
    /// Recursively walks the visual tree to find a FrameworkElement with a matching Tag.
    /// </summary>
    private static T? FindChildByTag<T>(DependencyObject parent, string tagValue) where T : FrameworkElement
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T element && element.Tag?.ToString() == tagValue)
                return element;

            var found = FindChildByTag<T>(child, tagValue);
            if (found != null)
                return found;
        }
        return null;
    }

    /// <summary>
    /// Global keyboard handler — registered on the XamlRoot.Content.
    /// This fires BEFORE any child control processes the key, guaranteeing we always
    /// intercept arrows, Tab, Enter, Escape no matter which element has XAML focus.
    /// </summary>
    private void OnGlobalPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        HandleGlobalKey(e);
    }

    /// <summary>
    /// Clears the PointerOver visual state from ALL GridViewItem containers.
    /// WinUI 3 never auto-clears PointerOver when navigation is keyboard-driven,
    /// because the mouse cursor physically stays on the same element.
    /// </summary>
    private void ClearAllPointerOverStates()
    {
        int selectedIndex = ViewModel.SelectedWindow != null
            ? ViewModel.Windows.IndexOf(ViewModel.SelectedWindow)
            : -1;

        for (int i = 0; i < ViewModel.Windows.Count; i++)
        {
            var container = WindowGrid.ContainerFromIndex(i) as GridViewItem;
            if (container != null)
            {
                VisualStateManager.GoToState(container, "Normal", true);
                VisualStateManager.GoToState(container, "Unselected", true);
            }
        }

        // Re-apply Selected state to the current item
        if (selectedIndex >= 0)
        {
            var selectedContainer = WindowGrid.ContainerFromIndex(selectedIndex) as GridViewItem;
            if (selectedContainer != null)
            {
                VisualStateManager.GoToState(selectedContainer, "Selected", true);
            }
        }
    }

    private void KeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        bool searchBoxFocused = ReferenceEquals(FocusManager.GetFocusedElement(this.XamlRoot), SearchBox);
        
        switch (args.KeyboardAccelerator.Key)
        {
            case Windows.System.VirtualKey.Left:
                ViewModel.NavigateWindowLeft();
                ClearAllPointerOverStates();
                ScrollSelectedWindowIntoView();
                if (searchBoxFocused)
                {
                    if (ViewModel.ShowLaunchSuggestions == Visibility.Visible)
                        SearchBox.Focus(FocusState.Programmatic);
                    else
                        this.Focus(FocusState.Programmatic);
                }
                args.Handled = true;
                break;
            case Windows.System.VirtualKey.Right:
                ViewModel.NavigateWindowRight();
                ClearAllPointerOverStates();
                ScrollSelectedWindowIntoView();
                if (searchBoxFocused)
                {
                    if (ViewModel.ShowLaunchSuggestions == Visibility.Visible)
                        SearchBox.Focus(FocusState.Programmatic);
                    else
                        this.Focus(FocusState.Programmatic);
                }
                args.Handled = true;
                break;
            case Windows.System.VirtualKey.Up:
                if (ViewModel.IsClipboardFolderSelected)
                {
                    ViewModel.NavigateClipboardUp();
                    ScrollSelectedClipboardItemIntoView();
                }
                else if (ViewModel.ShowLaunchSuggestions == Visibility.Visible)
                {
                    ViewModel.NavigateLaunchUp();
                    ScrollSelectedLaunchItemIntoView();
                }
                else
                {
                    ViewModel.NavigateWindowUp();
                    ClearAllPointerOverStates();
                    ScrollSelectedWindowIntoView();
                }
                if (searchBoxFocused) this.Focus(FocusState.Programmatic);
                args.Handled = true;
                break;
            case Windows.System.VirtualKey.Down:
                if (ViewModel.IsClipboardFolderSelected)
                {
                    ViewModel.NavigateClipboardDown();
                    ScrollSelectedClipboardItemIntoView();
                }
                else if (ViewModel.ShowLaunchSuggestions == Visibility.Visible)
                {
                    ViewModel.NavigateLaunchDown();
                    ScrollSelectedLaunchItemIntoView();
                }
                else
                {
                    ViewModel.NavigateWindowDown();
                    ClearAllPointerOverStates();
                    ScrollSelectedWindowIntoView();
                }
                if (searchBoxFocused) this.Focus(FocusState.Programmatic);
                args.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Core keyboard logic shared by both PreviewKeyDown and KeyDown handlers.
    /// </summary>
    private void HandleGlobalKey(KeyRoutedEventArgs e)
    {
        // Let the Add/Edit Folder dialog handle its own keys
        if (ViewModel.IsAddFolderPanelVisible)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                ViewModel.CancelAddFolder();
                e.Handled = true;
            }
            return;
        }
        if (ViewModel.IsEditFolderPanelVisible)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                ViewModel.CancelEditFolder();
                e.Handled = true;
            }
            return;
        }
        if (ViewModel.Settings.IsSettingsPanelVisible)
        {
            if (ViewModel.Settings.IsRecordingHotkey)
            {
                var key = e.Key;
                bool isModifierOnly = key is Windows.System.VirtualKey.Control
                                           or Windows.System.VirtualKey.LeftControl
                                           or Windows.System.VirtualKey.RightControl
                                           or Windows.System.VirtualKey.Shift
                                           or Windows.System.VirtualKey.LeftShift
                                           or Windows.System.VirtualKey.RightShift
                                           or Windows.System.VirtualKey.Menu
                                           or Windows.System.VirtualKey.LeftWindows
                                           or Windows.System.VirtualKey.RightWindows;
                if (!isModifierOnly)
                {
                    var mods = GetCurrentModifiers();
                    ViewModel.Settings.ApplyNewHotkey(mods, (uint)key);
                    e.Handled = true;
                }
                else
                {
                    e.Handled = true; // consume modifier-only presses while recording
                }
                return;
            }

            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                ViewModel.Settings.CancelSettings();
                e.Handled = true;
            }
            return;
        }

        bool searchBoxFocused = ReferenceEquals(FocusManager.GetFocusedElement(this.XamlRoot), SearchBox);

        // ── Ctrl+C copies the selected clipboard item when clipboard is visible ──
        var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
        bool ctrlDown = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        if (ctrlDown && e.Key == Windows.System.VirtualKey.C && ViewModel.IsClipboardFolderSelected)
        {
            ViewModel.CopySelectedClipboardItem();
            e.Handled = true;
            return;
        }

        // ── Auto-redirect printable chars to the search box ──
        // Only fires when focus is NOT already in the SearchBox.
        if (IsTypableKey(e.Key) && !searchBoxFocused)
        {
            var ch = VirtualKeyToChar(e.Key);
            if (ch != '\0')
            {
                // Start a fresh search with this character
                ViewModel.SearchQuery = ch.ToString();
                SearchBox.Focus(FocusState.Programmatic);
                // Place cursor at end so next keystrokes append naturally
                SearchBox.SelectionStart = 1;
                SearchBox.SelectionLength = 0;
            }
            else
            {
                SearchBox.Focus(FocusState.Programmatic);
            }
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Windows.System.VirtualKey.Up:
                if (ViewModel.IsClipboardFolderSelected)
                {
                    ViewModel.NavigateClipboardUp();
                    ScrollSelectedClipboardItemIntoView();
                }
                else if (ViewModel.ShowLaunchSuggestions == Visibility.Visible)
                {
                    ViewModel.NavigateLaunchUp();
                    ScrollSelectedLaunchItemIntoView();
                    if (searchBoxFocused)
                        SearchBox.Focus(FocusState.Programmatic);
                }
                else
                {
                    ViewModel.NavigateWindowUp();
                    ClearAllPointerOverStates();
                    ScrollSelectedWindowIntoView();
                    if (searchBoxFocused)
                        this.Focus(FocusState.Programmatic);
                }
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Down:
                if (ViewModel.IsClipboardFolderSelected)
                {
                    ViewModel.NavigateClipboardDown();
                    ScrollSelectedClipboardItemIntoView();
                }
                else if (ViewModel.ShowLaunchSuggestions == Visibility.Visible)
                {
                    ViewModel.NavigateLaunchDown();
                    ScrollSelectedLaunchItemIntoView();
                    if (searchBoxFocused)
                        SearchBox.Focus(FocusState.Programmatic);
                }
                else
                {
                    ViewModel.NavigateWindowDown();
                    ClearAllPointerOverStates();
                    ScrollSelectedWindowIntoView();
                    if (searchBoxFocused)
                        this.Focus(FocusState.Programmatic);
                }
                e.Handled = true;
                break;

            // Tab when search box is focused = toggle All / Apps mode
            // Tab otherwise = Navigate between folders
            case Windows.System.VirtualKey.Tab:
            {
                if (searchBoxFocused)
                {
                    ViewModel.SetAppSearchMode(!ViewModel.IsAppSearchMode);
                    // Keep the cursor in the search box
                    SearchBox.Focus(FocusState.Programmatic);
                    e.Handled = true;
                    break;
                }
                var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
                bool shiftDown = (shift & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
                if (shiftDown)
                    ViewModel.NavigateFolderUp();
                else
                    ViewModel.NavigateFolderDown();
                e.Handled = true;
                break;
            }

            case Windows.System.VirtualKey.Enter:
                if (ViewModel.ShowRecycleBinSuggestion)
                {
                    ViewModel.SelectRecycleBinFolder();
                    e.Handled = true;
                    break;
                }
                // When clipboard panel is visible, Enter copies the selected item
                if (ViewModel.IsClipboardFolderSelected)
                {
                    ViewModel.CopySelectedClipboardItem();
                    e.Handled = true;
                    break;
                }
                // If no windows match the search, use the launcher / run-fallback
                if (ViewModel.IsSearchActive && ViewModel.Windows.Count == 0)
                {
                    ViewModel.LaunchOrRun();
                    App.Current.HideOverlay();
                }
                else
                {
                    var switchHandle = ViewModel.SelectedWindow?.Model.Handle ?? IntPtr.Zero;
                    ViewModel.SwitchToWindow(); // handles virtual desktop switching
                    App.Current.HideOverlayAndSwitchTo(switchHandle);
                }
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Escape:
                // First Escape clears the search; second closes the overlay
                if (ViewModel.IsSearchActive)
                {
                    ViewModel.ClearSearch();
                    this.Focus(FocusState.Programmatic);
                }
                else
                {
                    App.Current.HideOverlay();
                }
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.F5:
                ViewModel.RefreshWindows();
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Delete:
                // Close the currently selected window
                if (ViewModel.SelectedWindow != null)
                {
                    ViewModel.CloseWindow();
                    RegisterDwmThumbnails();
                }
                e.Handled = true;
                break;
        }
    }

    private static bool IsTypableKey(Windows.System.VirtualKey key) => KeyboardHelper.IsTypableKey(key);
    private static char VirtualKeyToChar(Windows.System.VirtualKey key) => KeyboardHelper.VirtualKeyToChar(key);

    /// <summary>
    /// Click a window card to switch to it.
    /// </summary>
    private void WindowGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is WindowItemViewModel windowVm)
        {
            ViewModel.SelectedWindow = windowVm;
            ViewModel.SwitchToWindow(); // handles virtual desktop switching
            App.Current.HideOverlayAndSwitchTo(windowVm.Model.Handle);
        }
    }

    /// <summary>
    /// Reliable pointer activation for window cards. Using Tapped avoids cases
    /// where GridView ItemClick is not raised due to drag-enabled card content.
    /// </summary>
    private void WindowCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is WindowItemViewModel windowVm)
        {
            ViewModel.SelectedWindow = windowVm;
            ViewModel.SwitchToWindow(); // handles virtual desktop switching
            App.Current.HideOverlayAndSwitchTo(windowVm.Model.Handle);
            e.Handled = true;
        }
    }

    private void WindowGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        int gen = _thumbGeneration;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (gen != _thumbGeneration) return; // stale — a folder switch happened since
            UpdateWindowGridColumnCountFromLayout();
            RegisterDwmThumbnails();
        });
    }

    // ═══════════════════════════════════════════════════════════════
    // Window Card Context Menu Actions
    // ═══════════════════════════════════════════════════════════════

    private void WindowCard_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        // Store the DataContext now – before the flyout popup is shown – so
        // context menu handlers can retrieve the correct WindowItemViewModel.
        if (sender is FrameworkElement fe && fe.DataContext is WindowItemViewModel vm)
            _contextMenuTarget = vm;
    }

    private WindowItemViewModel? GetWindowFromContextMenu(object sender)
    {
        // Prefer the explicitly tracked target (reliable across all WinUI 3 scenarios).
        if (_contextMenuTarget != null)
            return _contextMenuTarget;
        // Fallback: try DataContext directly (may work in some layouts).
        if (sender is MenuFlyoutItem item && item.DataContext is WindowItemViewModel vm)
            return vm;
        return null;
    }

    private void RecordHotkey_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Settings.StartRecordHotkey();
        this.Focus(FocusState.Programmatic);
    }

    private void CancelRecordHotkey_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Settings.CancelRecordHotkey();
    }

    private static uint GetCurrentModifiers() => KeyboardHelper.GetCurrentModifiers();

    private void ContextMenu_SwitchTo(object sender, RoutedEventArgs e)
    {
        var vm = GetWindowFromContextMenu(sender);
        if (vm == null) return;
        ViewModel.SelectedWindow = vm;
        ViewModel.SwitchToWindow(); // handles virtual desktop switching
        App.Current.HideOverlayAndSwitchTo(vm.Model.Handle);
    }

    private void ContextMenu_Minimize(object sender, RoutedEventArgs e)
    {
        var vm = GetWindowFromContextMenu(sender);
        if (vm == null) return;
        ViewModel.MinimizeWindow(vm);
    }

    private void ContextMenu_Close(object sender, RoutedEventArgs e)
    {
        var vm = GetWindowFromContextMenu(sender);
        if (vm == null) return;
        ViewModel.CloseWindow(vm);
        RegisterDwmThumbnails();
    }

    private void ContextMenu_TogglePin(object sender, RoutedEventArgs e)
    {
        var vm = GetWindowFromContextMenu(sender);
        if (vm == null) return;
        ViewModel.TogglePinWindow(vm);
    }

    // ── Clipboard history handlers ──────────────────────────────

    private void ClipboardItem_Copy(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ClipboardItem item)
        {
            ViewModel.SelectedClipboardItem = item;
            ViewModel.CopySelectedClipboardItem();
        }
    }

    private void ClipboardItem_Remove(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ClipboardItem item)
            ViewModel.RemoveClipboardItem(item);
    }

    private void ClipboardItem_TogglePin(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ClipboardItem item)
            ViewModel.TogglePinClipboardItem(item);
    }

    private void PinnedClipboardItem_Copy(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ClipboardItem item)
        {
            ViewModel.SelectedClipboardItem = item;
            ViewModel.CopySelectedClipboardItem();
        }
    }

    private void PinnedClipboardItem_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ClipboardItem item)
        {
            ViewModel.SelectedClipboardItem = item;
            ViewModel.CopySelectedClipboardItem();
            e.Handled = true;
        }
    }

    private void SearchModeToggle_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SetAppSearchMode(!ViewModel.IsAppSearchMode);
        SearchBox.Focus(FocusState.Programmatic);
    }

    private void PinnedClipboardItem_Unpin(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ClipboardItem item)
            ViewModel.TogglePinClipboardItem(item);
    }

    // ── Composite rule condition remove handler ──────────────────

    private void RemoveRuleCondition_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is FolderRuleCondition condition)
            ViewModel.RemoveRuleCondition(condition);
    }

    // ═══════════════════════════════════════════════════════════════
    // Window Subclass — Clipboard Message Forwarding
    // ═══════════════════════════════════════════════════════════════

    private IntPtr SubclassWndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData)
    {
        const uint WM_CLIPBOARDUPDATE = 0x031D;
        if (uMsg == WM_CLIPBOARDUPDATE)
        {
            ViewModel.ProcessClipboardMessage(uMsg);
        }
        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    // ═══════════════════════════════════════════════════════════════
    // Drag & Drop — Window Cards → Folder List
    // ═══════════════════════════════════════════════════════════════

    private void WindowCard_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (sender is FrameworkElement fe && fe.DataContext is WindowItemViewModel vm)
        {
            args.Data.Properties["WindowItem"] = vm;
            args.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
        }
    }

    private void FolderItem_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Properties.ContainsKey("WindowItem")) return;

        if (sender is FrameworkElement fe && fe.DataContext is FolderItemViewModel folderVm)
        {
            if (folderVm.Model.Type == FolderType.All)
            {
                e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
                return;
            }

            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
            e.DragUIOverride.Caption = folderVm.Model.Type == FolderType.Manual
                ? "Add to folder"
                : "Create process folder";
            e.DragUIOverride.IsCaptionVisible = true;
        }
    }

    private void FolderItem_Drop(object sender, DragEventArgs e)
    {
        if (e.DataView.Properties.TryGetValue("WindowItem", out var obj)
            && obj is WindowItemViewModel windowVm
            && sender is FrameworkElement fe
            && fe.DataContext is FolderItemViewModel folderVm)
        {
            ViewModel.AssignWindowToFolder(windowVm, folderVm);
        }
    }

    /// <summary>
    /// Single click selects; the IsItemClickEnabled + ItemClick combination here
    /// means a single click launches — same UX as the Windows Start Menu.
    /// </summary>
    private void LaunchList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LaunchItemViewModel item)
        {
            ViewModel.SelectedLaunchItem = item;
            ViewModel.LaunchOrRun();
            App.Current.HideOverlay();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Folder Context Menu Actions
    // ═══════════════════════════════════════════════════════════════

    private void EditFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is FolderItemViewModel folder)
        {
            ViewModel.ShowEditFolderPanel(folder);
        }
    }

    private void DeleteFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is FolderItemViewModel folder)
        {
            ViewModel.DeleteFolder(folder);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Settings — Preset selector
    // ═══════════════════════════════════════════════════════════════

    private void PresetList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ThemePreset preset)
            ViewModel.Settings.ApplyPreset(preset);
    }

    private void CustomPresetList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ThemePreset preset)
            ViewModel.Settings.ApplyPreset(preset);
    }

    private void DeleteCustomPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ThemePreset preset)
            ViewModel.Settings.DeleteCustomPreset(preset);
    }

    /// <summary>
    /// Fires when the user clicks a nav item in the Settings left sidebar.
    /// Updates the active settings tab in the ViewModel.
    /// </summary>
    private void SettingsNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SettingsNav.SelectedItem is ListViewItem item && item.Tag is string tag)
            ViewModel.Settings.ActiveSettingsTab = tag;
    }

    // ═══════════════════════════════════════════════════════════════
    // Settings — Inline ColorPicker flyouts
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Guards against feedback loops when we programmatically set ColorPicker.Color.</summary>
    private bool _colorPickerSyncing;

    /// <summary>
    /// Fires when a color-swatch flyout opens.
    /// Pre-fills the embedded ColorPicker's color from the current ViewModel value.
    /// </summary>
    private void ColorFlyout_Opened(object sender, object e)
    {
        if (sender is Flyout flyout && flyout.Content is ColorPicker cp)
        {
            var hex = GetSettingsColor(cp.Tag?.ToString());
            _colorPickerSyncing = true;
            try { cp.Color = ParseHexToColorStatic(hex); }
            finally { _colorPickerSyncing = false; }
        }
    }

    /// <summary>
    /// Fires whenever the user picks a new color.
    /// Routes the result to the correct ViewModel property via the Tag.
    /// </summary>
    private void ColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_colorPickerSyncing) return;
        var key = sender.Tag?.ToString();
        var c = args.NewColor;
        // Include alpha channel for pickers with IsAlphaEnabled=true (folder overlay colors)
        var hex = sender.IsAlphaEnabled
            ? $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}"
            : $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        UpdateSettingsColor(key, hex);
    }

    private void LinkedIn_Click(object sender, RoutedEventArgs e)
    {
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://es.linkedin.com/in/sergiogm1999"));
    }

    private void GitHub_Click(object sender, RoutedEventArgs e)
    {
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/sgm1018/BetterWinTab"));
    }

    private string GetSettingsColor(string? key) => key switch
    {
        "Accent"        => ViewModel.Settings.SettingsAccentColor,
        "AccentDim"     => ViewModel.Settings.SettingsAccentDimColor,
        "AccentSubtle"  => ViewModel.Settings.SettingsAccentSubtleColor,
        "Background"    => ViewModel.Settings.SettingsBackgroundColor,
        "Surface"       => ViewModel.Settings.SettingsSurfaceColor,
        "Card"          => ViewModel.Settings.SettingsCardColor,
        "Border"        => ViewModel.Settings.SettingsBorderColor,
        "TextPrimary"   => ViewModel.Settings.SettingsTextPrimaryColor,
        "TextSecondary" => ViewModel.Settings.SettingsTextSecondaryColor,
        "TextMuted"     => ViewModel.Settings.SettingsTextMutedColor,
        "Danger"        => ViewModel.Settings.SettingsDangerColor,
        "FolderHover"   => ViewModel.Settings.SettingsFolderHoverColor,
        "FolderSelected" => ViewModel.Settings.SettingsFolderSelectedColor,
        "WindowHoverBorder" => ViewModel.Settings.SettingsWindowHoverBorderColor,
        "WindowHoverBackground" => ViewModel.Settings.SettingsWindowHoverBackgroundColor,
        _               => "#000000"
    };

    private void UpdateSettingsColor(string? key, string hex)
    {
        switch (key)
        {
            case "Accent":        ViewModel.Settings.SettingsAccentColor        = hex; break;
            case "AccentDim":     ViewModel.Settings.SettingsAccentDimColor     = hex; break;
            case "AccentSubtle":  ViewModel.Settings.SettingsAccentSubtleColor  = hex; break;
            case "Background":    ViewModel.Settings.SettingsBackgroundColor    = hex; break;
            case "Surface":       ViewModel.Settings.SettingsSurfaceColor       = hex; break;
            case "Card":          ViewModel.Settings.SettingsCardColor          = hex; break;
            case "Border":        ViewModel.Settings.SettingsBorderColor        = hex; break;
            case "TextPrimary":   ViewModel.Settings.SettingsTextPrimaryColor   = hex; break;
            case "TextSecondary": ViewModel.Settings.SettingsTextSecondaryColor = hex; break;
            case "TextMuted":     ViewModel.Settings.SettingsTextMutedColor     = hex; break;
            case "Danger":         ViewModel.Settings.SettingsDangerColor         = hex; break;
            case "FolderHover":    ViewModel.Settings.SettingsFolderHoverColor    = hex; break;
            case "FolderSelected": ViewModel.Settings.SettingsFolderSelectedColor = hex; break;
            case "WindowHoverBorder": ViewModel.Settings.SettingsWindowHoverBorderColor = hex; break;
            case "WindowHoverBackground": ViewModel.Settings.SettingsWindowHoverBackgroundColor = hex; break;
        }
    }

    private static Windows.UI.Color ParseHexToColorStatic(string hex)
    {
        try { return ThemeApplier.ParseHex(hex); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"MainPage.ParseHexToColorStatic: {ex.Message}"); }
        return Windows.UI.Color.FromArgb(255, 40, 40, 40);
    }

    // ═══════════════════════════════════════════════════════════════
    // Drag-and-Drop Folder Reordering
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Fires when the user begins dragging a folder item.
    /// Cancels drag for the "All Windows" folder (index 0).
    /// </summary>
    private void FolderList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.Count > 0 && e.Items[0] is FolderItemViewModel folder)
        {
            var idx = ViewModel.Folders.IndexOf(folder);
            if (idx == 0)
            {
                e.Cancel = true; // "All Windows" cannot be dragged
                return;
            }
            _draggedFolderOldIndex = idx;
        }
    }

    /// <summary>
    /// Fires after a drag-and-drop reorder completes on the folder list.
    /// Syncs the new order to the FolderService and persists to settings.
    /// </summary>
    private void FolderList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (args.Items.Count > 0 && args.Items[0] is FolderItemViewModel folder)
        {
            var newIndex = ViewModel.Folders.IndexOf(folder);

            // If someone managed to drop before "All Windows", push it back to index 1
            if (newIndex == 0)
            {
                ViewModel.Folders.Move(0, 1);
            }

            // Sync the visual order back to the service layer
            ViewModel.SyncFolderOrderToService();
        }
        _draggedFolderOldIndex = -1;
    }
}
