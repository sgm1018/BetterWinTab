using System.IO;
using System.Text.Json;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;

namespace BetterWinTab.Views;

/// <summary>
/// Notes panel: hosts the rich-text editor (Assets/NotesEditor) in WebView2 and
/// bridges it with <see cref="NotesViewModel"/> through web messages.
/// </summary>
public sealed partial class MainPage
{
    private const string NotesHostName = "notes.betterwintab";
    private static readonly string NotesEditorUrl = $"https://{NotesHostName}/editor.html";

    private bool _notesEditorInitStarted;
    private bool _notesEditorReady;
    private string? _pendingNotesFocus;

    private void InitializeNotesPanel()
    {
        ViewModel.Notes.SelectedNoteChanged += SendNoteToEditor;
        ViewModel.Notes.FocusEditorRequested += () => RequestEditorFocus("title");
        ViewModel.Settings.AppearanceChanged += SendThemeToEditor;

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsNotesFolderSelected) && ViewModel.IsNotesFolderSelected)
            {
                ViewModel.Notes.RefreshTimestamps();
                _ = EnsureNotesEditorAsync();
            }
        };
    }

    private async Task EnsureNotesEditorAsync()
    {
        if (_notesEditorInitStarted) return;
        _notesEditorInitStarted = true;

        try
        {
            // Unpackaged apps default the WebView2 profile next to the exe, which is not
            // writable under Program Files — keep it in LocalAppData instead.
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BetterWinTab", "WebView2");
            Directory.CreateDirectory(userDataFolder);
            Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR", "FF000000");

            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(
                string.Empty, userDataFolder, new CoreWebView2EnvironmentOptions());
            await NotesWebView.EnsureCoreWebView2Async(environment);

            var core = NotesWebView.CoreWebView2;
            var settings = core.Settings;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.AreDevToolsEnabled = System.Diagnostics.Debugger.IsAttached;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.IsGeneralAutofillEnabled = false;
            settings.IsPasswordAutosaveEnabled = false;
            settings.IsWebMessageEnabled = true;

            core.SetVirtualHostNameToFolderMapping(
                NotesHostName,
                Path.Combine(AppContext.BaseDirectory, "Assets", "NotesEditor"),
                CoreWebView2HostResourceAccessKind.DenyCors);

            core.WebMessageReceived += NotesWebView_WebMessageReceived;
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                OpenExternalLink(args.Uri);
            };
            core.NavigationStarting += (_, args) =>
            {
                if (args.Uri.StartsWith($"https://{NotesHostName}/", StringComparison.OrdinalIgnoreCase)) return;
                args.Cancel = true;
                OpenExternalLink(args.Uri);
            };
            core.ProcessFailed += (_, _) => DispatcherQueue.TryEnqueue(() =>
            {
                _notesEditorReady = false;
                NotesWebView.Opacity = 0;
                NotesEditorStatus.Text = "The notes editor stopped unexpectedly. Reloading...";
                NotesEditorStatus.Visibility = Visibility.Visible;
                NotesWebView.CoreWebView2?.Reload();
            });

            NotesWebView.Source = new Uri(NotesEditorUrl);
        }
        catch (Exception ex)
        {
            _notesEditorInitStarted = false; // retry next time the panel is opened
            NotesEditorStatus.Text =
                "The notes editor requires the Microsoft Edge WebView2 Runtime.\n" +
                "Install it from https://developer.microsoft.com/microsoft-edge/webview2 and reopen Notes.\n\n" +
                ex.Message;
            NotesEditorStatus.Visibility = Visibility.Visible;
            System.Diagnostics.Debug.WriteLine($"Notes editor init failed: {ex}");
        }
    }

    private void NotesWebView_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!args.Source.StartsWith($"https://{NotesHostName}/", StringComparison.OrdinalIgnoreCase)) return;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(args.WebMessageAsJson); }
        catch (JsonException) { return; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            string Str(string name) =>
                root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

            switch (Str("type"))
            {
                case "ready":
                    _notesEditorReady = true;
                    SendThemeToEditor();
                    SendNoteToEditor(ViewModel.Notes.SelectedNote);
                    NotesWebView.Opacity = 1;
                    NotesEditorStatus.Visibility = Visibility.Collapsed;
                    if (_pendingNotesFocus != null) RequestEditorFocus(_pendingNotesFocus);
                    break;

                case "change":
                    ViewModel.Notes.ApplyEditorChange(Str("id"), Str("title"), Str("html"), Str("font"), Str("preview"));
                    break;

                case "newNote":
                    ViewModel.Notes.NewNote();
                    break;

                case "open":
                    OpenExternalLink(Str("url"));
                    break;

                case "escape":
                    // Leave the editor; the next Escape (handled by the page) closes the overlay.
                    this.Focus(FocusState.Programmatic);
                    break;
            }
        }
    }

    private void SendNoteToEditor(NoteItemViewModel? note)
    {
        if (!_notesEditorReady || NotesWebView.CoreWebView2 == null) return;
        var payload = note == null
            ? JsonSerializer.Serialize(new { type = "load", id = (string?)null, title = "", html = "", font = "handwritten" })
            : JsonSerializer.Serialize(new
            {
                type = "load",
                id = note.Model.Id,
                title = note.Model.Title,
                html = note.Model.Html,
                font = note.Model.Font,
            });
        NotesWebView.CoreWebView2.PostWebMessageAsJson(payload);
    }

    private void SendThemeToEditor()
    {
        if (!_notesEditorReady || NotesWebView.CoreWebView2 == null) return;
        var a = ViewModel.GetAppearanceSettings();
        var payload = JsonSerializer.Serialize(new
        {
            type = "theme",
            accent = a.AccentColor,
            accentDim = a.AccentDimColor,
            accentSubtle = a.AccentSubtleColor,
            background = a.BackgroundColor,
            surface = a.SurfaceColor,
            card = a.CardColor,
            border = a.BorderColor,
            text = a.TextPrimaryColor,
            textSecondary = a.TextSecondaryColor,
            muted = a.TextMutedColor,
            danger = a.DangerColor,
        });
        NotesWebView.CoreWebView2.PostWebMessageAsJson(payload);
    }

    /// <summary>Moves keyboard focus into the editor ("title" or "body").</summary>
    private void RequestEditorFocus(string target)
    {
        if (!_notesEditorReady || NotesWebView.CoreWebView2 == null)
        {
            _pendingNotesFocus = target;
            return;
        }
        _pendingNotesFocus = null;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            NotesWebView.Focus(FocusState.Programmatic);
            NotesWebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "focus", target }));
        });
    }

    /// <summary>Asks the editor to push pending edits and writes notes to disk.</summary>
    private void FlushNotes()
    {
        if (_notesEditorReady)
            NotesWebView.CoreWebView2?.PostWebMessageAsJson("{\"type\":\"flush\"}");
        ViewModel.Notes.Flush();
    }

    private bool IsNotesEditorFocused()
    {
        if (!ViewModel.IsNotesFolderSelected || XamlRoot == null) return false;
        return ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), NotesWebView);
    }

    private static void OpenExternalLink(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri) ||
            !Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeMailto))
            return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(parsed.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OpenExternalLink: {ex.Message}");
        }
    }

    private void ScrollSelectedNoteIntoView()
    {
        if (ViewModel.Notes.SelectedNote != null)
            NotesList.ScrollIntoView(ViewModel.Notes.SelectedNote, ScrollIntoViewAlignment.Default);
    }

    // ── Sidebar item handlers ───────────────────────────────────────────────

    private void NoteItem_Clone(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: NoteItemViewModel note })
            ViewModel.Notes.CloneNote(note);
    }

    private void NoteItem_Delete(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: NoteItemViewModel note })
            ViewModel.Notes.DeleteNote(note);
    }
}
