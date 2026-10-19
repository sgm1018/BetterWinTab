using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace BetterWinTab.Services;

/// <summary>Local, current-Windows-user-only transport. Never opens a network port.</summary>
public sealed class NotesBridgeService : IDisposable
{
    public const string PipeName = "BetterWinTab.Notes.v1";
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Func<JsonElement, Task<object>> _execute;
    private readonly string _pipeName;
    private readonly Task _listener;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public NotesBridgeService(Func<JsonElement, Task<object>> execute, string pipeName = PipeName)
    {
        _execute = execute;
        _pipeName = pipeName;
        _listener = ListenAsync();
    }

    private async Task ListenAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_shutdown.Token).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                // One request per connection. Read with a bound before parsing untrusted input.
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                var input = new StringBuilder();
                var buffer = new char[1];
                while (true)
                {
                    if (await reader.ReadAsync(buffer.AsMemory(), timeout.Token).ConfigureAwait(false) == 0)
                        throw new IOException("Connection closed before request.");
                    if (buffer[0] == '\n') break;
                    if (input.Length >= 1_000_000) throw new IOException("Request too large.");
                    input.Append(buffer[0]);
                }

                object response;
                try
                {
                    using var document = JsonDocument.Parse(input.ToString());
                    response = new { ok = true, result = await _execute(document.RootElement.Clone()).ConfigureAwait(false) };
                }
                catch (Exception ex)
                {
                    response = new { ok = false, error = ex.Message };
                }
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions).AsMemory(), timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NotesBridgeService: {ex.Message}");
                try { await Task.Delay(250, _shutdown.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    public void Dispose() => _shutdown.Cancel();
}
