using BetterWinTab.Models;
using NAudio.CoreAudioApi;
using System.Runtime.InteropServices;
using System.Text;

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
                    string.Equals(device.ID, defaultId, StringComparison.OrdinalIgnoreCase),
                    RequiresHandsFreeProfile(device.ID)))
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

    public static bool RequiresHandsFreeProfile(string endpointId)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var instanceId = endpointId.StartsWith("SWD\\", StringComparison.OrdinalIgnoreCase)
            ? endpointId : "SWD\\MMDEVAPI\\" + endpointId;
        if (CM_Locate_DevNode(out var node, instanceId, 0) != 0) return false;
        for (var depth = 0; depth < 12; depth++)
        {
            var id = new StringBuilder(1024);
            if (CM_Get_Device_ID(node, id, id.Capacity, 0) != 0) break;
            if (id.ToString().StartsWith("BTHHFENUM\\", StringComparison.OrdinalIgnoreCase)) return true;
            if (CM_Get_Parent(out var parent, node, 0) != 0 || parent == node) break;
            node = parent;
        }
        return false;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Locate_DevNodeW")]
    private static extern int CM_Locate_DevNode(out uint node, string deviceId, uint flags);
    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(out uint parent, uint node, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Get_Device_IDW")]
    private static extern int CM_Get_Device_ID(uint node, StringBuilder buffer, int length, uint flags);
}
