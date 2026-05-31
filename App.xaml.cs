using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Navigation;
using Windows.Graphics;
using WinRT.Interop;
using BetterWinTab.Services;
using BetterWinTab.Models;
using BetterWinTab.Interop;

namespace BetterWinTab
{
    /// <summary>
    /// BetterWinTab — Application entry point.
    /// Configures the window as a borderless fullscreen overlay.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;
        private AppWindow? _appWindow;
        private IntPtr _hwnd;
        private HotkeyService? _hotkeyService;
        private SettingsService? _settingsService;
        private AppSettings? _settings;

        public static new App Current => (App)Application.Current;
        public Window? MainWindow => _window;
        public AppWindow? AppWin => _appWindow;
        public IntPtr Hwnd => _hwnd;
        public HotkeyService? Hotkey => _hotkeyService;

        public App()
        {
            this.InitializeComponent();

            var logPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BetterWinTab", "crash.log");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
            void Log(string tag, object? ex) =>
                System.IO.File.AppendAllText(logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {tag}: {ex}\n\n");

            this.UnhandledException += (s, e) => { Log("XAML", e.Exception); e.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log("AppDomain", e.ExceptionObject);
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) => { Log("Task", e.Exception); e.SetObserved(); };
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            _window = new Window();

            // Get AppWindow for advanced window management
            _hwnd = WindowNative.GetWindowHandle(_window);
            var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);

            // ── Prevent startup flash ──
            // Move window off-screen and make it tiny before activating
            _appWindow.MoveAndResize(new RectInt32(-32000, -32000, 1, 1));

            // ── Register services in DI container ──
            // Must happen BEFORE Navigate, since MainPage's view-model
            // resolves services from the container in its constructor.
            RegisterServices();

            // Setup content
            var rootFrame = new Frame();
            rootFrame.NavigationFailed += OnNavigationFailed;
            _window.Content = rootFrame;
            rootFrame.Navigate(typeof(Views.MainPage));

            // Activate (required by WinUI 3 to initialize XAML tree, but off-screen)
            _window.Activate();

            // Immediately hide so the 1x1 off-screen window never flashes
            _appWindow.Hide();

            // Now configure as borderless overlay (sets correct size/position for when we Show)
            ConfigureOverlayWindow();

            // Install low-level keyboard hook for Ctrl+Tab
            _settingsService = ServiceContainer.Resolve<SettingsService>();
            _settings = _settingsService.Load();
            _hotkeyService = new HotkeyService();
            _hotkeyService.Install(_window.DispatcherQueue);
            _hotkeyService.Configure(_settings.HotkeyModifiers, _settings.HotkeyVKey);
            _hotkeyService.HotkeyPressed += OnHotkeyPressed;
            _hotkeyService.HideOverlayRequested += HideOverlay;
            _hotkeyService.IsOverlayVisible = () => _appWindow?.IsVisible == true;

            // Show the overlay immediately on startup.
            _window?.DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => ShowOverlay());
        }

        private void RegisterServices()
        {
            ServiceContainer.RegisterSingleton(() => new SettingsService());
            ServiceContainer.RegisterSingleton(() => new WindowEnumerationService());
            ServiceContainer.RegisterSingleton(() => new FolderService(ServiceContainer.Resolve<WindowEnumerationService>()));
            ServiceContainer.RegisterSingleton(() => new LaunchService());
            ServiceContainer.RegisterSingleton(() => new VirtualDesktopService());
            ServiceContainer.RegisterSingleton(() => new ClipboardService());
            ServiceContainer.RegisterSingleton(() => new UpdateService());
        }

        /// <summary>
        /// Configures the window as a borderless, fullscreen, topmost overlay.
        /// Uses Win32 APIs to strip all border/caption styles for a truly borderless window.
        /// </summary>
        private void ConfigureOverlayWindow()
        {
            if (_appWindow == null) return;

            // ── WinUI 3 presenter config ──
            _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;

            var presenter = _appWindow.Presenter as OverlappedPresenter;
            if (presenter != null)
            {
                presenter.IsAlwaysOnTop = true;
                presenter.IsResizable = false;
                presenter.IsMinimizable = false;
                presenter.IsMaximizable = false;
                presenter.SetBorderAndTitleBar(false, false);
            }

            // ── Win32: Strip ALL window chrome styles for truly borderless ──
            var style = (long)NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_STYLE);
            style &= ~(long)(NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME
                           | NativeMethods.WS_BORDER | NativeMethods.WS_SYSMENU);
            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_STYLE, (IntPtr)style);

            // Apply frame change so the new style takes effect
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_FRAMECHANGED);

            // ── Fullscreen: use OuterBounds to cover entire screen including taskbar ──
            var displayArea = DisplayArea.GetFromWindowId(
                _appWindow.Id, DisplayAreaFallback.Primary);

            if (displayArea != null)
            {
                var bounds = displayArea.OuterBounds; // Full screen, NOT WorkArea
                _appWindow.MoveAndResize(new RectInt32(
                    bounds.X, bounds.Y,
                    bounds.Width, bounds.Height));
            }
        }

        /// <summary>
        /// Toggle overlay visibility on hotkey press.
        /// </summary>
        private void OnHotkeyPressed()
        {
            if (_appWindow == null) return;

            if (_appWindow.IsVisible)
            {
                HideOverlay();
            }
            else
            {
                ShowOverlay();
            }
        }

        public void ExitApplication()
        {
            try
            {
                if (_window?.Content is Frame frame && frame.Content is Views.MainPage page)
                    page.OnOverlayHidden();
            }
            catch
            {
                // Best-effort cleanup only.
            }

            try
            {
                _hotkeyService?.Dispose();
            }
            catch
            {
                // Best-effort cleanup only.
            }

            try
            {
                _window?.Close();
            }
            catch
            {
                // Exit still terminates the process even if the window is already closing.
            }

            Exit();
        }

        public void HideOverlay()
        {
            // Unregister DWM thumbnails before hiding
            if (_window?.Content is Frame frame && frame.Content is Views.MainPage page)
            {
                page.OnOverlayHidden();
            }
            _appWindow?.Hide();
        }

        /// <summary>
        /// Hides the overlay and, after it is fully hidden, forces focus to <paramref name="hwnd"/>.
        /// Calling SetForegroundWindow BEFORE Hide causes Windows to reassign focus when the
        /// overlay disappears. Dispatching the focus call at Low priority ensures it runs after
        /// the Hide message has been processed by the message pump.
        /// </summary>
        public void HideOverlayAndSwitchTo(IntPtr hwnd)
        {
            if (_window?.Content is Frame frame && frame.Content is Views.MainPage page)
                page.OnOverlayHidden();

            _appWindow?.Hide();

            if (hwnd == IntPtr.Zero) return;

            _window?.DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (NativeMethods.IsIconic(hwnd))
                        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
                    ForceForeground(hwnd);
                });
        }

        public void ShowOverlay()
        {
            _appWindow?.Show();
            _window?.Activate();

            // ── Force foreground + topmost via Win32 ──
            ForceForeground(_hwnd);

            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);

            // Notify UI to refresh and grab focus
            if (_window?.Content is Frame frame && frame.Content is Views.MainPage page)
            {
                page.OnOverlayShown();
            }

            // Second Win32 focus attempt after UI has processed (Low priority = after layout)
            _window?.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                ForceForeground(_hwnd);

                // Re-activate to ensure WinUI XAML tree gets keyboard input
                _window?.Activate();

                // Now that Win32+WinUI are both focused, push XAML focus to the page
                if (_window?.Content is Frame f && f.Content is Views.MainPage p)
                {
                    p.ForceFocusToPage();
                }
            });
        }

        /// <summary>
        /// Forces a window to the foreground using the AttachThreadInput trick.
        /// Windows restricts SetForegroundWindow to the thread that owns the current
        /// foreground window. By temporarily attaching our input to that thread, we
        /// gain permission to steal focus.
        /// </summary>
        private static void ForceForeground(IntPtr hwnd)
        {
            var foregroundHwnd = NativeMethods.GetForegroundWindow();
            if (foregroundHwnd == IntPtr.Zero)
            {
                NativeMethods.SetForegroundWindow(hwnd);
                NativeMethods.SetFocus(hwnd);
                return;
            }

            uint foregroundThreadId = NativeMethods.GetWindowThreadProcessId(foregroundHwnd, out _);
            uint currentThreadId = NativeMethods.GetCurrentThreadId();

            if (foregroundThreadId != currentThreadId)
            {
                NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, true);
                NativeMethods.SetForegroundWindow(hwnd);
                NativeMethods.SetFocus(hwnd);
                NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }
            else
            {
                NativeMethods.SetForegroundWindow(hwnd);
                NativeMethods.SetFocus(hwnd);
            }
        }

        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }
    }
}
