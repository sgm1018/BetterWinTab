using BetterWinTab.Models;
using NAudio.CoreAudioApi;

namespace BetterWinTab.Services;

/// <summary>Lists active Windows audio endpoints for microphone and render loopback capture.</summary>
public sealed class AudioDeviceService
{
    public IReadOnlyList<AudioDeviceInfo> EnumerateMicrophones() => Enumerate(DataFlow.Capture, AudioCaptureSource.Microphone);

    public IReadOnlyList<AudioDeviceInfo> EnumerateLoopbackDevices() => Enumerate(DataFlow.Render, AudioCaptureSource.Loopback);

    public IReadOnlyList<AudioDeviceInfo> EnumerateAll() =>
        EnumerateMicrophones().Concat(EnumerateLoopbackDevices()).ToArray();

    private static IReadOnlyList<AudioDeviceInfo> Enumerate(DataFlow flow, AudioCaptureSource source)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var defaultDevice = TryGetDefault(enumerator, flow,
            flow == DataFlow.Capture ? Role.Communications : Role.Multimedia);
        var defaultId = defaultDevice?.ID;
        var devices = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
        try
        {
            return devices
                .Select(device => new AudioDeviceInfo(device.ID, device.FriendlyName, source,
                    string.Equals(device.ID, defaultId, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
        }
        finally
        {
            foreach (var device in devices)
                device.Dispose();
        }
    }

    private static MMDevice? TryGetDefault(MMDeviceEnumerator enumerator, DataFlow flow, Role role)
    {
        try
        {
            return enumerator.GetDefaultAudioEndpoint(flow, role);
        }
        catch
        {
            return null;
        }
    }
}
