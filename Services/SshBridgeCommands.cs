using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BetterWinTab.Models;

namespace BetterWinTab.Services;

internal static class SshBridgeCommands
{
    private const string CreateAction = "create_ssh_machine";
    private static readonly byte[] PasswordContext = Encoding.UTF8.GetBytes("BetterWinTab SSH MCP v1");
    private static readonly UTF8Encoding PasswordEncoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static bool Handles(JsonElement request)
        => request.ValueKind == JsonValueKind.Object
            && request.TryGetProperty("action", out var action)
            && action.ValueKind == JsonValueKind.String
            && action.ValueEquals(CreateAction);

    public static object Execute(JsonElement request, Func<string?, string, int, string, string, SshMachine> create)
    {
        if (!Handles(request) || !request.TryGetProperty("arguments", out var args) || args.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Se esperaban los datos de una máquina SSH.");
        foreach (var property in args.EnumerateObject())
        {
            if (property.Name is not ("name" or "host" or "port" or "username" or "passwordProtected"))
                throw new ArgumentException("Argumento de SSH desconocido.");
        }

        var name = ReadString(args, "name", 100);
        var host = ReadString(args, "host", 64) ?? throw new ArgumentException("Falta la dirección IP.");
        var username = ReadString(args, "username", 255) ?? throw new ArgumentException("Falta el usuario SSH.");
        var blob = ReadString(args, "passwordProtected", 16384) ?? throw new ArgumentException("Falta la contraseña protegida.");
        var port = 22;
        if (args.TryGetProperty("port", out var portValue)
            && (portValue.ValueKind != JsonValueKind.Number || !portValue.TryGetInt32(out port) || port is < 1 or > 65535))
            throw new ArgumentException("El puerto debe estar entre 1 y 65535.");

        var password = UnprotectPassword(blob);
        try
        {
            var machine = create(name, host, port, username, password);
            return new { machine.Id, machine.Name, machine.Host, machine.Port, machine.Username };
        }
        catch (Exception)
        {
            throw new InvalidOperationException("No se pudo guardar la máquina SSH. Comprueba la IP, el usuario y la contraseña.");
        }
    }

    private static string UnprotectPassword(string blob)
    {
        try
        {
            var plaintext = ProtectedData.Unprotect(Convert.FromBase64String(blob), PasswordContext, DataProtectionScope.CurrentUser);
            try
            {
                return PasswordEncoding.GetString(plaintext);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or DecoderFallbackException)
        {
            throw new ArgumentException("La contraseña SSH protegida no es válida para este usuario de Windows.");
        }
    }

    private static string? ReadString(JsonElement args, string name, int maxLength)
    {
        if (!args.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } text || text.Length > maxLength)
            throw new ArgumentException($"El argumento {name} no es válido.");
        return text;
    }
}
