using Microsoft.Win32;
using System.Text.Json;

namespace BetterWinTab.Services;

public sealed class BrowserNativeHostInstaller
{
    private const string HostName = "com.betterwintab.tabs";
    private const string HostExecutableName = "BetterWinTab.NativeHost.exe";

    public string Install(IReadOnlyDictionary<string, string> extensionIds)
    {
        var validIds = extensionIds
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value.Trim(), StringComparer.OrdinalIgnoreCase);

        if (validIds.Count == 0)
            return "Enter at least one Chromium extension ID.";

        var invalid = validIds.Values.FirstOrDefault(id => !IsExtensionId(id));
        if (invalid != null)
            return $"Invalid extension ID: {invalid}";

        var sourceDirectory = FindHostDirectory();
        if (sourceDirectory == null)
            return "Native host not found. Build BrowserExtension\\NativeHost in Release first.";

        var installDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BetterWinTab",
            "NativeHost");
        Directory.CreateDirectory(installDirectory);
        CopyHostFiles(sourceDirectory, installDirectory);

        var manifestPath = Path.Combine(installDirectory, $"{HostName}.json");
        var manifest = new
        {
            name = HostName,
            description = "BetterWinTab Chromium tab bridge",
            path = Path.Combine(installDirectory, HostExecutableName),
            type = "stdio",
            allowed_origins = validIds.Values
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(id => $"chrome-extension://{id}/")
                .ToArray()
        };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        foreach (var registryPath in RegistryPaths())
        {
            using var key = Registry.CurrentUser.CreateSubKey(registryPath);
            key?.SetValue(string.Empty, manifestPath, RegistryValueKind.String);
        }

        return $"Native host registered for {validIds.Count} browser extension(s). Restart the browser and BetterWinTab.";
    }

    private static IEnumerable<string> RegistryPaths()
    {
        yield return $@"Software\Google\Chrome\NativeMessagingHosts\{HostName}";
        yield return $@"Software\Microsoft\Edge\NativeMessagingHosts\{HostName}";
        yield return $@"Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\{HostName}";
    }

    private static bool IsExtensionId(string value) =>
        value.Length == 32 && value.All(character => character is >= 'a' and <= 'p');

    private static void CopyHostFiles(string sourceDirectory, string destinationDirectory)
    {
        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory))
            File.Copy(sourceFile, Path.Combine(destinationDirectory, Path.GetFileName(sourceFile)), true);
    }

    private static string? FindHostDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var index = 0; index < 10 && current != null; index++, current = current.Parent)
        {
            var releaseDirectory = Path.Combine(current.FullName, "BrowserExtension", "NativeHost", "bin", "Release", "net8.0");
            if (File.Exists(Path.Combine(releaseDirectory, HostExecutableName)))
                return releaseDirectory;

            var localHostDirectory = Path.Combine(current.FullName, "BrowserExtension", "NativeHost");
            if (File.Exists(Path.Combine(localHostDirectory, HostExecutableName)))
                return localHostDirectory;
        }

        var installedDirectory = Path.Combine(AppContext.BaseDirectory, "BrowserExtension", "NativeHost");
        return File.Exists(Path.Combine(installedDirectory, HostExecutableName)) ? installedDirectory : null;
    }
}
