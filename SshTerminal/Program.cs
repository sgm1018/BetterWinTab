using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using BetterWinTab.Services;
using BetterWinTab.SshTerminal;
using Renci.SshNet;
using Renci.SshNet.Common;

if (args.Length != 1 || !Guid.TryParseExact(args[0], "N", out var sessionId))
    return 2;

if (Console.IsInputRedirected || Console.IsOutputRedirected)
    return 2;

Console.InputEncoding = Console.OutputEncoding = new UTF8Encoding(false);
SshTerminalRequest? request = null;
string? hostKeyError = null;
try
{
    using (var pipe = new NamedPipeClientStream(".", SshTerminalRequest.GetPipeName(sessionId),
               PipeDirection.In, PipeOptions.CurrentUserOnly))
    {
        await pipe.ConnectAsync(8000);
        request = SshTerminalRequest.Read(pipe);
    }

    var endpoint = request.Host.Contains(':') ? $"[{request.Host}]:{request.Port}" : $"{request.Host}:{request.Port}";
    Console.Title = $"SSH · {endpoint}";
    Console.WriteLine($"Connecting to {endpoint}…");
    var knownHostsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterWinTab", "ssh-known-hosts");
    var hostKeys = new HostKeyStore(knownHostsPath);

    using var authentication = new PasswordAuthenticationMethod(request.Username, request.Password);
    var connection = new ConnectionInfo(request.Host, request.Port, request.Username, authentication)
    {
        Timeout = TimeSpan.FromSeconds(90)
    };
    using var client = new SshClient(connection) { KeepAliveInterval = TimeSpan.FromSeconds(30) };
    client.HostKeyReceived += (_, key) =>
    {
        key.CanTrust = false;
        try
        {
            key.CanTrust = hostKeys.Verify(endpoint, key.FingerPrintSHA256, () =>
            {
                Console.WriteLine($"First connection to {endpoint}.");
                Console.WriteLine($"{key.HostKeyName} fingerprint: SHA256:{key.FingerPrintSHA256}");
                Console.Write("Verify the fingerprint with the server administrator. Trust this key? [y/N] ");
                return string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase);
            });
            if (!key.CanTrust) hostKeyError = "The SSH host key was not accepted.";
        }
        catch (InvalidOperationException ex)
        {
            hostKeyError = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            hostKeyError = "Could not read or save the SSH host key.";
        }
    };

    try
    {
        client.Connect();
    }
    finally
    {
        CryptographicOperations.ZeroMemory(request.Password);
    }

    using var session = new ConsoleSession();
    await session.RunAsync(client);
    return 0;
}
catch (Exception ex)
{
    // Avoid exposing exception details supplied by a remote server or local credential data.
    Console.Error.WriteLine(hostKeyError ?? (ex switch
    {
        SshAuthenticationException => "The server rejected the saved username or password.",
        SshOperationTimeoutException or TimeoutException => "The SSH connection timed out.",
        _ => "Could not open or continue the SSH session."
    }));
    Console.Error.Write("Press Enter to close. ");
    Console.ReadLine();
    return 1;
}
finally
{
    if (request is not null)
        CryptographicOperations.ZeroMemory(request.Password);
}
