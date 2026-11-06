using System.IO;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Launcher = Windows.System.Launcher;

namespace BetterWinTab.Controls;

public sealed partial class MarkdownPreview : UserControl
{
    private const string ViewerHost = "calls.betterwintab.local";
    private const string ViewerUrl = "https://" + ViewerHost + "/CallsMarkdown/index.html";
    private readonly DispatcherQueueTimer _renderTimer;
    private readonly DispatcherQueueTimer _readyTimer;
    private bool _initializing;
    private bool _ready;
    private bool _failed;

    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown), typeof(string), typeof(MarkdownPreview),
        new PropertyMetadata(string.Empty, OnMarkdownChanged));

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value ?? string.Empty);
    }

    public MarkdownPreview()
    {
        InitializeComponent();
        _renderTimer = DispatcherQueue.CreateTimer();
        _renderTimer.Interval = TimeSpan.FromMilliseconds(80);
        _renderTimer.IsRepeating = false;
        _renderTimer.Tick += (_, _) => SendMarkdown();
        _readyTimer = DispatcherQueue.CreateTimer();
        _readyTimer.Interval = TimeSpan.FromSeconds(15);
        _readyTimer.IsRepeating = false;
        _readyTimer.Tick += (_, _) => ShowFallback();
        ActualThemeChanged += (_, _) => QueueRender();
    }

    private static void OnMarkdownChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((MarkdownPreview)sender).QueueRender();

    private void QueueRender()
    {
        // Start once per interval so a continuous stream cannot postpone rendering.
        if (_ready && IsLoaded && !_renderTimer.IsRunning) _renderTimer.Start();
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_ready) { QueueRender(); return; }
        if (_initializing || _failed) return;
        _initializing = true;
        try
        {
            var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BetterWinTab", "WebView2");
            Directory.CreateDirectory(profile);
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(
                string.Empty, profile, new CoreWebView2EnvironmentOptions());
            await Viewer.EnsureCoreWebView2Async(environment);
            var core = Viewer.CoreWebView2;
            var settings = core.Settings;
            settings.AreBrowserAcceleratorKeysEnabled = false; // Copy and text selection remain enabled.
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.IsGeneralAutofillEnabled = false;
            settings.IsPasswordAutosaveEnabled = false;
            settings.IsWebMessageEnabled = true;
            core.SetVirtualHostNameToFolderMapping(ViewerHost, Path.Combine(AppContext.BaseDirectory, "Assets"),
                CoreWebView2HostResourceAccessKind.DenyCors);
            core.WebMessageReceived += OnWebMessage;
            core.NavigationStarting += (_, navigation) =>
            {
                if (IsViewerDocument(navigation.Uri)) return;
                navigation.Cancel = true;
                if (navigation.IsUserInitiated) OpenExternalLink(navigation.Uri);
            };
            core.NewWindowRequested += (_, request) =>
            {
                request.Handled = true;
                if (request.IsUserInitiated) OpenExternalLink(request.Uri);
            };
            core.PermissionRequested += (_, request) => request.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, download) => download.Cancel = true;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, request) =>
            {
                if (!IsViewerAsset(request.Request.Uri))
                    request.Response = environment.CreateWebResourceResponse(null, 403, "Blocked", "");
            };
            core.ProcessFailed += (_, _) => DispatcherQueue.TryEnqueue(ShowFallback);
            core.NavigationCompleted += (_, navigation) =>
            {
                if (!navigation.IsSuccess) ShowFallback();
            };
            _readyTimer.Start();
            Viewer.Source = new Uri(ViewerUrl);
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"Markdown preview initialization failed: {error}");
            ShowFallback();
        }
        finally { _initializing = false; }
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _renderTimer.Stop();
        // Keep this WebView alive across tab changes; it retains selection and scroll.
    }

    private void OnWebMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!IsViewerDocument(args.Source)) return;
        try
        {
            using var document = JsonDocument.Parse(args.WebMessageAsJson);
            var message = document.RootElement;
            if (message.ValueKind != JsonValueKind.Object ||
                !message.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) return;
            switch (type.GetString())
            {
                case "ready" when !_failed:
                    _readyTimer.Stop();
                    _ready = true;
                    SendMarkdown();
                    if (!_ready) return;
                    Viewer.Opacity = 1;
                    Viewer.IsHitTestVisible = true;
                    Fallback.Visibility = Visibility.Collapsed;
                    break;
                case "open" when _ready:
                    if (message.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String)
                        OpenExternalLink(url.GetString());
                    break;
            }
        }
        catch (JsonException) { }
    }

    private void SendMarkdown()
    {
        if (!_ready || Viewer.CoreWebView2 == null) return;
        try
        {
            Viewer.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
            {
                type = "render",
                markdown = Markdown ?? string.Empty,
                theme = new
                {
                    background = BrushColor("PureBlackBrush", "#000000"),
                    surface = BrushColor("DarkSurfaceBrush", "#0a0a0a"),
                    text = BrushColor("TextPrimaryBrush", "#eeeeee"),
                    secondary = BrushColor("TextSecondaryBrush", "#aaaaaa"),
                    border = BrushColor("BorderDarkBrush", "#292929"),
                    accent = BrushColor("NeonGreenBrush", "#b6d3ff")
                }
            }));
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"Markdown preview update failed: {error}");
            ShowFallback();
        }
    }

    private static string BrushColor(string key, string fallback)
    {
        if (Application.Current.Resources.TryGetValue(key, out var resource) && resource is SolidColorBrush brush)
        {
            var color = brush.Color;
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        return fallback;
    }

    private static bool IsViewerDocument(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           uri.Scheme == Uri.UriSchemeHttps && uri.Host == ViewerHost && uri.IsDefaultPort &&
           uri.AbsolutePath == "/CallsMarkdown/index.html" && uri.Query.Length == 0;

    private static bool IsViewerAsset(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Host != ViewerHost || !uri.IsDefaultPort || uri.Query.Length != 0) return false;
        return uri.AbsolutePath is "/CallsMarkdown/index.html" or "/CallsMarkdown/viewer.css" or
            "/CallsMarkdown/bootstrap.js" or "/CallsMarkdown/renderer.js" or "/CallsMarkdown/viewer.js" or
            "/NotesEditor/js/markdown.js";
    }

    private static async void OpenExternalLink(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) || uri.Host == ViewerHost) return;
        try { await Launcher.LaunchUriAsync(uri); }
        catch (Exception error) { System.Diagnostics.Debug.WriteLine($"Could not open notes link: {error}"); }
    }

    private void ShowFallback()
    {
        _ready = false;
        _failed = true;
        _renderTimer.Stop();
        _readyTimer.Stop();
        Viewer.Opacity = 0;
        Viewer.IsHitTestVisible = false;
        FallbackNotice.Visibility = Visibility.Visible;
        Fallback.Visibility = Visibility.Visible;
    }
}
