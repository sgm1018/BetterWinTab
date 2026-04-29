using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterWinTab.Services;

/// <summary>
/// Checks GitHub Releases for a newer version and downloads/launches the installer.
///
/// Configuration:
///   • Set <see cref="GitHubOwner"/> and <see cref="GitHubRepo"/> to your repository.
///
/// Security notes:
///   • All network calls use HTTPS exclusively; non-HTTPS URLs are rejected.
///   • The downloaded file path is sanitised with Path.GetFileName to prevent path
///     traversal from a malformed asset name.
///   • No signature verification is performed beyond the HTTPS transport guarantee.
///     Consider adding a SHA-256 checksum asset to your releases for stronger integrity.
///   • Uses HttpWebRequest (System.Net) instead of HttpClient to avoid introducing
///     a new System.Net.Http assembly reference that would cause the WinUI 3 XAML
///     compiler (a .NET Fx 4.7.2 process) to fail when hashing the intermediate DLL.
/// </summary>
public sealed partial class UpdateService
{
    // ── Configuration ─────────────────────────────────────────────────────────
    public static string CurrentVersion { get; } = ResolveCurrentVersion();
    private const string GitHubOwner = "sgm1018";
    private const string GitHubRepo = "BetterWinTab";

    private const string ApiUrl =
        $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

    // ── State ─────────────────────────────────────────────────────────────────
    public bool IsUpdateAvailable { get; private set; }
    public string LatestVersion { get; private set; } = CurrentVersion;
    public string? InstallerDownloadUrl => _installerDownloadUrl;

    private string? _installerDownloadUrl;

    /// <summary>Raised on the thread-pool thread when a new version is found.</summary>
    public event Action? StateChanged;

    public sealed record DownloadProgress(string Message, int? Percentage = null, bool IsIndeterminate = false);

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Checks GitHub for a newer release.  Designed to be fire-and-forgotten at
    /// startup; never throws — all errors are silently swallowed so the app starts
    /// cleanly even when the network is unavailable.
    /// </summary>
    public async Task CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var request = CreateRequest(ApiUrl);
            using var response = (HttpWebResponse)await request
                .GetResponseAsync().ConfigureAwait(false);

            using var stream = response.GetResponseStream()
                ?? throw new InvalidOperationException("Empty response stream.");

            var release = await JsonSerializer
                .DeserializeAsync(stream, UpdateServiceJsonContext.Default.GitHubRelease, ct)
                .ConfigureAwait(false);

            if (release is null) return;

            var remote = release.TagName?.TrimStart('v', 'V') ?? string.Empty;
            if (!IsNewer(remote, CurrentVersion)) return;

            LatestVersion = remote;
            _installerDownloadUrl = FindInstallerUrl(release);
            IsUpdateAvailable = true;

            StateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update check failed: {ex}");
            // Network errors, timeouts, rate-limiting, JSON parse failures — ignore.
        }
    }

    /// <summary>
    /// Downloads the installer to %TEMP% and launches it with ShellExecute so
    /// the installer's own UAC prompt (if any) is shown to the user.
    /// Returns <c>false</c> if no installer asset URL was found in the release.
    /// </summary>
    public async Task<bool> DownloadAndInstallAsync(
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_installerDownloadUrl)) return false;

        progress?.Report(new DownloadProgress("Preparing installer download...", 0, true));

        // Sanitise the file name — Path.GetFileName strips any directory components.
        var uriPath = new Uri(_installerDownloadUrl).LocalPath;
        var fileName = Path.GetFileName(uriPath);
        if (string.IsNullOrEmpty(fileName))
            fileName = $"BetterWinTab-Setup-{LatestVersion}-x64.exe";

        var destination = Path.Combine(Path.GetTempPath(), fileName);

        progress?.Report(new DownloadProgress("Connecting to the release server...", 0, true));

        var request = CreateDownloadRequest(_installerDownloadUrl);
        using var response = (HttpWebResponse)await request
            .GetResponseAsync().ConfigureAwait(false);

        var total = response.ContentLength;   // -1 if unknown
        var hasKnownLength = total > 0;
        long bytes = 0;
        var lastReportedPercentage = -1;

        progress?.Report(new DownloadProgress(
            hasKnownLength ? "Downloading installer..." : "Downloading installer (size unknown)...",
            hasKnownLength ? 0 : null,
            !hasKnownLength));

        using (var src = response.GetResponseStream()
            ?? throw new InvalidOperationException("Empty download stream."))
        using (var dest = new FileStream(
            destination, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await src.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
            {
                await dest.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                bytes += read;
                if (!hasKnownLength) continue;

                var percentage = (int)(bytes * 100 / total);
                if (percentage == lastReportedPercentage) continue;

                lastReportedPercentage = percentage;
                progress?.Report(new DownloadProgress("Downloading installer...", percentage));
            }

            await dest.FlushAsync(ct).ConfigureAwait(false);
        }

        progress?.Report(new DownloadProgress("Download complete. Launching installer...", 100));

        // Launch with UseShellExecute so the OS handles UAC elevation if required.
        Process.Start(new ProcessStartInfo(destination) { UseShellExecute = true });
        progress?.Report(new DownloadProgress("Installer opened. Follow the installation steps.", 100));
        return true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a pre-configured <see cref="HttpWebRequest"/> for the GitHub API.
    /// All network calls go over HTTPS to prevent man-in-the-middle attacks.
    /// </summary>
    private static HttpWebRequest CreateRequest(string url)
    {
        // Validate HTTPS — reject non-HTTPS URLs as a defence-in-depth measure.
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only HTTPS URLs are permitted.", nameof(url));

#pragma warning disable SYSLIB0014
        var request = (HttpWebRequest)WebRequest.Create(url);
#pragma warning restore SYSLIB0014
        request.Timeout = 15_000;   // 15 s connect+read timeout
        // GitHub API requires a User-Agent header; the token format is "app/version".
        request.UserAgent = $"BetterWinTab-Updater/{CurrentVersion}";
        request.Accept = "application/vnd.github+json";
        request.Headers["X-GitHub-Api-Version"] = "2022-11-28";
        return request;
    }

    /// <summary>
    /// Creates a <see cref="HttpWebRequest"/> for downloading a large file.
    /// Unlike <see cref="CreateRequest"/>, this sets <see cref="System.Net.HttpWebRequest.Timeout"/> to
    /// <see cref="System.Threading.Timeout.Infinite"/> so a multi-megabyte installer does not time out.
    /// </summary>
    private static HttpWebRequest CreateDownloadRequest(string url)
    {
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only HTTPS URLs are permitted.", nameof(url));

#pragma warning disable SYSLIB0014
        var request = (HttpWebRequest)WebRequest.Create(url);
#pragma warning restore SYSLIB0014
        // No timeout — the download of a multi-MB release asset may take minutes on
        // a slow connection.  The user's CancellationToken is the only deadline.
        request.Timeout = System.Threading.Timeout.Infinite;
        request.UserAgent = $"BetterWinTab-Updater/{CurrentVersion}";
        request.AllowAutoRedirect = true;
        return request;
    }

    private static string ResolveCurrentVersion()
    {
        var entryAssembly = Assembly.GetEntryAssembly();

        var informationalVersion = entryAssembly
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var normalizedInformationalVersion = informationalVersion
                .Split('+', 2)[0]
                .TrimStart('v', 'V');

            if (Version.TryParse(normalizedInformationalVersion, out _))
                return normalizedInformationalVersion;
        }

        var assemblyVersion = entryAssembly?.GetName().Version;
        if (assemblyVersion is null)
            return "0.0.0";

        return string.Join('.', new[]
        {
            assemblyVersion.Major,
            assemblyVersion.Minor,
            Math.Max(assemblyVersion.Build, 0)
        });
    }

    /// <summary>Returns <c>true</c> when <paramref name="remote"/> is strictly newer than <paramref name="local"/>.</summary>
    private static bool IsNewer(string remote, string local)
    {
        if (Version.TryParse(remote, out var r) && Version.TryParse(local, out var l))
            return r > l;
        // Fallback: treat any tag difference as "newer" rather than crashing.
        return !string.Equals(remote, local, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Selects the best installer asset from the release, preferring x64.
    /// Falls back to any .exe asset if no architecture-specific one is found.
    /// </summary>
    private static string? FindInstallerUrl(GitHubRelease release)
    {
        if (release.Assets is not { Length: > 0 }) return null;

        // Prefer x64 installer, then arm64, then any .exe.
        foreach (var suffix in new[] { "x64", "arm64" })
        {
            var match = Array.Find(release.Assets, a =>
                a.Name?.Contains(suffix, StringComparison.OrdinalIgnoreCase) == true &&
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            if (match?.BrowserDownloadUrl is not null)
                return match.BrowserDownloadUrl;
        }

        var any = Array.Find(release.Assets,
            a => a.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true);
        return any?.BrowserDownloadUrl;
    }

    // ── GitHub REST API DTOs (minimal — only the fields we actually read) ─────

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("assets")]
        public GitHubAsset[]? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }

    // Trim-safe source-generated serializer context.
    // Avoids reflection-based deserialization that breaks when PublishTrimmed=true.
    [JsonSerializable(typeof(GitHubRelease))]
    private sealed partial class UpdateServiceJsonContext : JsonSerializerContext { }
}
