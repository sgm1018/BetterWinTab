using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterWinTab.Services;

public sealed partial class UpdateService
{
    public static string CurrentVersion { get; } = ResolveCurrentVersion();
    private const string GitHubOwner = "sgm1018";
    private const string GitHubRepo = "BetterWinTab";

    private const string ApiUrl =
        $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

    public bool IsUpdateAvailable { get; private set; }
    public string LatestVersion { get; private set; } = CurrentVersion;
    public string? InstallerDownloadUrl => _installerDownloadUrl;

    private string? _installerDownloadUrl;
    private static readonly HttpClient _httpClient = CreateHttpClient();

    public event Action? StateChanged;

    public sealed record DownloadProgress(string Message, int? Percentage = null, bool IsIndeterminate = false);

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true
        };
        var client = new HttpClient(handler);
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"BetterWinTab-Updater/{CurrentVersion}");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    public async Task CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(ApiUrl, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
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
        }
    }

    public async Task<bool> DownloadAndInstallAsync(
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_installerDownloadUrl)) return false;

        progress?.Report(new DownloadProgress("Preparing installer download...", 0, true));

        var uriPath = new Uri(_installerDownloadUrl).LocalPath;
        var fileName = Path.GetFileName(uriPath);
        if (string.IsNullOrEmpty(fileName))
            fileName = $"BetterWinTab-Setup-{LatestVersion}-x64.exe";

        var destination = Path.Combine(Path.GetTempPath(), fileName);

        progress?.Report(new DownloadProgress("Connecting to the release server...", 0, true));

        using var downloadClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
        downloadClient.DefaultRequestHeaders.UserAgent.ParseAdd($"BetterWinTab-Updater/{CurrentVersion}");
        downloadClient.Timeout = System.Threading.Timeout.InfiniteTimeSpan;

        using var response = await downloadClient.GetAsync(_installerDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1;
        var hasKnownLength = total > 0;
        long bytes = 0;
        var lastReportedPercentage = -1;

        progress?.Report(new DownloadProgress(
            hasKnownLength ? "Downloading installer..." : "Downloading installer (size unknown)...",
            hasKnownLength ? 0 : null,
            !hasKnownLength));

        using (var src = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
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

        Process.Start(new ProcessStartInfo(destination) { UseShellExecute = true });
        progress?.Report(new DownloadProgress("Installer opened. Follow the installation steps.", 100));
        return true;
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

    private static bool IsNewer(string remote, string local)
    {
        if (Version.TryParse(remote, out var r) && Version.TryParse(local, out var l))
            return r > l;
        return !string.Equals(remote, local, StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindInstallerUrl(GitHubRelease release)
    {
        if (release.Assets is not { Length: > 0 }) return null;

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

    [JsonSerializable(typeof(GitHubRelease))]
    private sealed partial class UpdateServiceJsonContext : JsonSerializerContext { }
}
