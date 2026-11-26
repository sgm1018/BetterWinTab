using System.Runtime.InteropServices;
using Renci.SshNet;

namespace BetterWinTab.SshTerminal;

internal sealed class ConsoleSession : IDisposable
{
    private const uint EnableRawInput = 0x0200;
    private const uint CookedInputFlags = 0x0001 | 0x0002 | 0x0004;
    private const uint EnableVirtualTerminalOutput = 0x0001 | 0x0004;

    private readonly IntPtr _inputHandle = GetStdHandle(-10);
    private readonly IntPtr _outputHandle = GetStdHandle(-11);
    private readonly uint _inputMode;
    private readonly uint _outputMode;

    internal ConsoleSession()
    {
        if (!GetConsoleMode(_inputHandle, out _inputMode) ||
            !GetConsoleMode(_outputHandle, out _outputMode))
        {
            throw new InvalidOperationException("The SSH session requires an interactive Windows console.");
        }

        if (!SetConsoleMode(_outputHandle, _outputMode | EnableVirtualTerminalOutput) ||
            !SetConsoleMode(_inputHandle, (_inputMode & ~CookedInputFlags) | EnableRawInput))
        {
            Dispose();
            throw new InvalidOperationException("Could not configure the Windows terminal for SSH.");
        }
    }

    internal async Task RunAsync(SshClient client)
    {
        var initialSize = GetWindowSize();
        using var shell = client.CreateShellStream("xterm-256color", initialSize.Columns, initialSize.Rows, 0, 0, 16 * 1024);
        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();
        using var lifetime = new CancellationTokenSource();
        var cancellationToken = lifetime.Token;

        // A console read is allowed to block on its own thread until the helper exits.
        var inputTask = Task.Run(() => CopyInput(input, shell, cancellationToken));
        var resizeTask = MonitorSizeAsync(shell, initialSize, cancellationToken);
        try
        {
            shell.CopyTo(output);
            if (inputTask.IsCompleted)
                await inputTask.ConfigureAwait(false);
        }
        finally
        {
            lifetime.Cancel();
            await resizeTask.ConfigureAwait(false);
        }
    }

    private static void CopyInput(Stream input, ShellStream shell, CancellationToken cancellationToken)
    {
        try
        {
            var buffer = new byte[4096];
            int length;
            while ((length = input.Read(buffer)) > 0 && !cancellationToken.IsCancellationRequested)
            {
                shell.Write(buffer, 0, length);
                shell.Flush();
            }
        }
        catch (ObjectDisposedException)
        {
            // The remote shell or the local terminal closed during a write.
        }
        finally
        {
            shell.Dispose();
        }
    }

    private static async Task MonitorSizeAsync(
        ShellStream shell, (uint Columns, uint Rows) size, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var nextSize = GetWindowSize();
                if (nextSize == size) continue;
                shell.ChangeWindowSize(nextSize.Columns, nextSize.Rows, 0, 0);
                size = nextSize;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) { }
    }

    private static (uint Columns, uint Rows) GetWindowSize() =>
        ((uint)Math.Max(1, Console.WindowWidth), (uint)Math.Max(1, Console.WindowHeight));

    public void Dispose()
    {
        SetConsoleMode(_inputHandle, _inputMode);
        SetConsoleMode(_outputHandle, _outputMode);
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int handle);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConsoleMode(IntPtr handle, out uint mode);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleMode(IntPtr handle, uint mode);
}
