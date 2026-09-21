using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

const string pipeName = "BetterWinTab.BrowserTabs";
var parentProcessId = GetParentProcessId();
using var appPipe = await ConnectToAppPipeAsync(pipeName);

using var appReader = new StreamReader(appPipe, Encoding.UTF8, leaveOpen: true);
using var appWriter = new StreamWriter(appPipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

var sendToBrowser = Task.Run(async () =>
{
    while (true)
    {
        var message = await appReader.ReadLineAsync();
        if (message == null) break;
        await WriteNativeMessageAsync(Console.OpenStandardOutput(), message);
    }
});

_ = sendToBrowser.ContinueWith(
    _ => Environment.Exit(0),
    CancellationToken.None,
    TaskContinuationOptions.ExecuteSynchronously,
    TaskScheduler.Default);

while (true)
{
    var message = await ReadNativeMessageAsync(Console.OpenStandardInput());
    if (message == null) break;

    try
    {
        var json = JsonNode.Parse(message)?.AsObject();
        if (json == null) continue;
        json["browserProcessId"] = parentProcessId;
        await appWriter.WriteLineAsync(json.ToJsonString());
    }
    catch
    {
        // Ignore malformed messages from the browser and keep the host alive.
    }
}

return 0;

static async Task<string?> ReadNativeMessageAsync(Stream input)
{
    var header = new byte[4];
    var read = await ReadExactlyAsync(input, header);
    if (!read) return null;

    var length = BitConverter.ToInt32(header, 0);
    if (length <= 0 || length > 16 * 1024 * 1024) return null;

    var payload = new byte[length];
    if (!await ReadExactlyAsync(input, payload)) return null;
    return Encoding.UTF8.GetString(payload);
}

static async Task<NamedPipeClientStream> ConnectToAppPipeAsync(string pipeName)
{
    while (true)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(5000);
            return pipe;
        }
        catch
        {
            pipe.Dispose();
            await Task.Delay(1000);
        }
    }
}

static async Task WriteNativeMessageAsync(Stream output, string message)
{
    var payload = Encoding.UTF8.GetBytes(message);
    await output.WriteAsync(BitConverter.GetBytes(payload.Length));
    await output.WriteAsync(payload);
    await output.FlushAsync();
}

static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buffer)
{
    var offset = 0;
    while (offset < buffer.Length)
    {
        var count = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset));
        if (count == 0) return false;
        offset += count;
    }
    return true;
}

static int GetParentProcessId()
{
    using var current = Process.GetCurrentProcess();
    var status = NtQueryInformationProcess(
        current.Handle,
        0,
        out var info,
        Marshal.SizeOf<ProcessBasicInformation>(),
        out _);
    return status == 0 ? info.InheritedFromUniqueProcessId.ToInt32() : 0;
}

[DllImport("ntdll.dll")]
static extern int NtQueryInformationProcess(
    IntPtr processHandle,
    int processInformationClass,
    out ProcessBasicInformation processInformation,
    int processInformationLength,
    out int returnLength);

[StructLayout(LayoutKind.Sequential)]
struct ProcessBasicInformation
{
    public IntPtr Reserved1;
    public IntPtr PebBaseAddress;
    public IntPtr Reserved2_0;
    public IntPtr Reserved2_1;
    public IntPtr UniqueProcessId;
    public IntPtr InheritedFromUniqueProcessId;
}
