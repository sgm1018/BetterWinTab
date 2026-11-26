using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BetterWinTab.Models;

namespace BetterWinTab.Services;

/// <summary>Stores the SSH catalog and encrypts credentials with a key protected by Windows DPAPI.</summary>
public sealed class SshMachineService
{
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly string _storePath;
    private readonly string _keyPath;
    private readonly string _mutexName;

    public SshMachineService()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BetterWinTab", "SSH"))
    {
    }

    internal SshMachineService(string directory)
    {
        directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        _storePath = Path.Combine(directory, "machines.json");
        _keyPath = Path.Combine(directory, "key.dpapi");
        var directoryHash = SHA256.HashData(Encoding.UTF8.GetBytes(directory.ToUpperInvariant()));
        _mutexName = "Local\\BetterWinTab.Ssh." + Convert.ToHexString(directoryHash);
    }

    public event EventHandler? MachinesChanged;

    public IReadOnlyList<SshMachine> GetMachines() =>
        WithStore(machines => machines.Select(entry => entry.Machine).ToArray());

    public SshMachine Create(string? name, string host, int port, string username, string password)
    {
        var machine = ValidateMachine(Guid.NewGuid().ToString("N"), name, host, port, username);
        ValidatePassword(password);
        WithStore(machines =>
        {
            using var cipher = OpenCipher(canCreateKey: machines.Count == 0);
            machines.Add(Encrypt(machine, password, cipher));
            SaveMachines(machines);
            return machine;
        });
        MachinesChanged?.Invoke(this, EventArgs.Empty);
        return machine;
    }

    public SshMachine Update(string id, string? name, string host, int port, string username, string? password)
    {
        var machine = ValidateMachine(id, name, host, port, username);
        if (password is not null) ValidatePassword(password);
        WithStore(machines =>
        {
            var index = machines.FindIndex(entry => entry.Machine.Id == id);
            if (index < 0) throw new InvalidOperationException("This SSH machine no longer exists.");

            using var cipher = OpenCipher(canCreateKey: false);
            var previous = machines[index];
            machines[index] = Encrypt(machine, password ?? Decrypt(previous, cipher), cipher);
            SaveMachines(machines);
            return machine;
        });
        MachinesChanged?.Invoke(this, EventArgs.Empty);
        return machine;
    }

    public bool Delete(string id)
    {
        var deleted = WithStore(machines =>
        {
            if (machines.RemoveAll(entry => entry.Machine.Id == id) == 0) return false;
            SaveMachines(machines);
            return true;
        });
        if (deleted) MachinesChanged?.Invoke(this, EventArgs.Empty);
        return deleted;
    }

    internal string GetPassword(SshMachine machine) => WithStore(machines =>
    {
        var entry = machines.Find(candidate => candidate.Machine == machine)
            ?? throw new InvalidOperationException("This SSH machine changed. Select it again.");
        using var cipher = OpenCipher(canCreateKey: false);
        return Decrypt(entry, cipher);
    });

    private static SshMachine ValidateMachine(string id, string? name, string host, int port, string username)
    {
        if (!IPAddress.TryParse(host.Trim(), out var ipAddress))
            throw new ArgumentException("Enter a valid IPv4 or IPv6 address.");
        if (port is < 1 or > 65535)
            throw new ArgumentException("Enter a port between 1 and 65535.");

        username = username.Trim();
        if (username.Length is 0 or > 255 || username.Any(char.IsControl))
            throw new ArgumentException("Enter a valid SSH username.");

        name = name?.Trim();
        if (name is { Length: > 100 } || name?.Any(char.IsControl) == true)
            throw new ArgumentException("Enter a name of up to 100 characters.");

        var address = ipAddress.ToString();
        return new SshMachine(id, string.IsNullOrEmpty(name) ? address : name, address, port, username);
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password) || Encoding.UTF8.GetByteCount(password) > 4096)
            throw new ArgumentException("Enter an SSH password of up to 4096 UTF-8 bytes.");
    }

    private T WithStore<T>(Func<List<StoredMachine>, T> action)
    {
        using var mutex = new Mutex(false, _mutexName);
        try
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(5)))
                throw new IOException("The SSH catalog is busy. Try again.");
        }
        catch (AbandonedMutexException)
        {
            // The previous owner exited. Atomic replacements keep the last saved catalog intact.
        }

        try
        {
            var machines = File.Exists(_storePath)
                ? JsonSerializer.Deserialize<List<StoredMachine>>(File.ReadAllBytes(_storePath))
                    ?? throw new IOException("The SSH catalog could not be read.")
                : [];
            return action(machines);
        }
        catch (JsonException ex)
        {
            throw new IOException("The SSH catalog could not be read.", ex);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private AesGcm OpenCipher(bool canCreateKey)
    {
        byte[] key;
        if (File.Exists(_keyPath))
        {
            key = ProtectedData.Unprotect(File.ReadAllBytes(_keyPath), null, DataProtectionScope.CurrentUser);
        }
        else if (canCreateKey)
        {
            key = RandomNumberGenerator.GetBytes(KeySize);
        }
        else
        {
            throw new CryptographicException("The SSH encryption key is missing.");
        }

        try
        {
            if (key.Length != KeySize) throw new CryptographicException("The SSH encryption key is invalid.");
            if (!File.Exists(_keyPath))
                WriteAtomically(_keyPath, ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser));
            return new AesGcm(key, TagSize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static StoredMachine Encrypt(SshMachine machine, string password, AesGcm cipher)
    {
        var plaintext = Encoding.UTF8.GetBytes(password);
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagSize];
            cipher.Encrypt(nonce, plaintext, ciphertext, tag, EndpointData(machine));
            return new StoredMachine(machine, nonce, ciphertext, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static string Decrypt(StoredMachine entry, AesGcm cipher)
    {
        var plaintext = new byte[entry.Ciphertext.Length];
        try
        {
            cipher.Decrypt(entry.Nonce, entry.Ciphertext, entry.Tag, plaintext, EndpointData(entry.Machine));
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static byte[] EndpointData(SshMachine machine) => Encoding.UTF8.GetBytes(string.Join('\n',
        machine.Id, machine.Host, machine.Port.ToString(CultureInfo.InvariantCulture), machine.Username));

    private void SaveMachines(List<StoredMachine> machines) =>
        WriteAtomically(_storePath, JsonSerializer.SerializeToUtf8Bytes(machines));

    private static void WriteAtomically(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private sealed record StoredMachine(SshMachine Machine, byte[] Nonce, byte[] Ciphertext, byte[] Tag);
}
