using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using BetterWinTab.Models;
using Microsoft.Win32.SafeHandles;

namespace BetterWinTab.Services;

public sealed class SshTerminalService
{
    private static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(12);

    public async Task OpenAsync(SshMachine machine, string password, CancellationToken cancellationToken = default)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "SshTerminal", "BetterWinTab.SshTerminal.exe");
        if (!File.Exists(executable))
            throw new InvalidOperationException("The SSH terminal is missing. Repair or reinstall BetterWinTab.");

        cancellationToken.ThrowIfCancellationRequested();
        var sessionId = Guid.NewGuid();
        await using var pipe = new NamedPipeServerStream(
            SshTerminalRequest.GetPipeName(sessionId), PipeDirection.Out, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        startInfo.ArgumentList.Add(sessionId.ToString("N"));
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the SSH terminal.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(LaunchTimeout);
        try
        {
            await pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var clientProcessId) ||
                clientProcessId != (uint)process.Id)
            {
                throw new InvalidOperationException("Could not verify the SSH terminal process.");
            }

            var passwordBytes = Encoding.UTF8.GetBytes(password);
            try
            {
                new SshTerminalRequest(machine.Host, machine.Port, machine.Username, passwordBytes).Write(pipe);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("The SSH terminal did not respond in time.");
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);
}
