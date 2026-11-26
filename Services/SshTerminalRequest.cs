using System.IO;
using System.Text;

namespace BetterWinTab.Services;

internal sealed record SshTerminalRequest(string Host, int Port, string Username, byte[] Password)
{
    internal static string GetPipeName(Guid sessionId) => $"BetterWinTab.SshTerminal.{sessionId:N}";

    internal void Write(Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Host);
        writer.Write(Port);
        writer.Write(Username);
        writer.Write(Password.Length);
        writer.Write(Password);
    }

    internal static SshTerminalRequest Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var host = reader.ReadString();
        var port = reader.ReadInt32();
        var username = reader.ReadString();
        var passwordLength = reader.ReadInt32();
        if (passwordLength is < 1 or > 64 * 1024)
            throw new InvalidDataException("Invalid SSH credential length.");
        var password = reader.ReadBytes(passwordLength);
        if (password.Length != passwordLength)
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(password);
            throw new EndOfStreamException("The SSH credential was incomplete.");
        }

        return new SshTerminalRequest(host, port, username, password);
    }
}
