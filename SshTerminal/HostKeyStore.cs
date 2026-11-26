using System.Security.Cryptography;
using System.Text;

namespace BetterWinTab.SshTerminal;

internal sealed class HostKeyStore(string directory)
{
    internal bool Verify(string endpoint, string fingerprint, Func<bool> confirmFirstUse)
    {
        var endpointHash = SHA256.HashData(Encoding.UTF8.GetBytes(endpoint));
        var path = Path.Combine(directory, Convert.ToHexString(endpointHash) + ".key");
        if (File.Exists(path))
            return IsSameFingerprint(path, fingerprint);
        if (!confirmFirstUse())
            return false;

        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporaryPath, fingerprint);
            try
            {
                File.Move(temporaryPath, path);
                return true;
            }
            catch (IOException) when (File.Exists(path))
            {
                // A concurrent first connection already saved its verified key.
                return IsSameFingerprint(path, fingerprint);
            }
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static bool IsSameFingerprint(string path, string fingerprint)
    {
        if (string.Equals(File.ReadAllText(path), fingerprint, StringComparison.Ordinal))
            return true;
        throw new InvalidOperationException("This server's SSH host key has changed. Verify the change with its administrator.");
    }
}
