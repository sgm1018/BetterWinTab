using System.IO;
using System.Text.Json;
using BetterWinTab.Models;
using Microsoft.Win32;

namespace BetterWinTab.Services;

/// <summary>
/// Persists application settings to a JSON file in AppData.
/// </summary>
public class SettingsService
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRegistryName = "BetterWinTab";

    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BetterWinTab");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Loads settings from disk, or returns defaults.
    /// </summary>
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SettingsService.Load: {ex.Message}");
        }

        return new AppSettings();
    }

    /// <summary>
    /// Saves settings to disk.
    /// </summary>
    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SettingsService.Save: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns true if the app is registered to run at Windows startup.
    /// </summary>
    public bool GetRunAtStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(AppRegistryName) != null;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"SettingsService.GetRunAtStartup: {ex.Message}"); return false; }
    }

    /// <summary>
    /// Adds or removes the app from the Windows startup registry key.
    /// </summary>
    public void SetRunAtStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key == null) return;

            if (enable)
            {
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                    key.SetValue(AppRegistryName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(AppRegistryName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"SettingsService.SetRunAtStartup: {ex.Message}"); }
    }
}
