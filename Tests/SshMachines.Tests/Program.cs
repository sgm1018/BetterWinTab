using System.Security.Cryptography;
using System.Text.Json;
using BetterWinTab.Services;

var directory = Directory.CreateTempSubdirectory("betterwintab-ssh-test-");
try
{
    const string password = " local test password ☃ ";
    var service = new SshMachineService(directory.FullName);
    int notifications = 0;
    service.MachinesChanged += (_, _) => notifications++;

    var machine = service.Create("Lab", "2001:0db8::1", 2222, "operator", password);
    var statePath = Path.Combine(directory.FullName, "machines.json");
    var keyPath = Path.Combine(directory.FullName, "key.dpapi");
    Require(notifications == 1 && machine.Host == "2001:db8::1", "Create notifies and normalizes IPv6");
    Require(!File.ReadAllText(statePath).Contains(password, StringComparison.Ordinal)
        && !JsonSerializer.Serialize(machine).Contains("Password", StringComparison.Ordinal),
        "Metadata and persisted state hide the password");

    var storedKey = File.ReadAllBytes(keyPath);
    var rawKey = ProtectedData.Unprotect(storedKey, null, DataProtectionScope.CurrentUser);
    Require(rawKey.Length == 32 && storedKey.Length > 32, "The local key is protected with DPAPI");
    CryptographicOperations.ZeroMemory(rawKey);

    var reloaded = new SshMachineService(directory.FullName);
    Require(reloaded.GetMachines().Single() == machine && reloaded.GetPassword(machine) == password,
        "A fresh service can decrypt the saved password");
    Throws<ArgumentException>(() => service.Create(null, "example.invalid", 22, "user", password));
    Throws<ArgumentException>(() => service.Create(null, "192.0.2.1", 65536, "user", password));

    var edited = service.Update(machine.Id, null, "192.0.2.4", 22, "admin", null);
    Require(edited.Name == "192.0.2.4" && service.GetPassword(edited) == password,
        "Editing an endpoint preserves and re-encrypts its password");
    Throws<InvalidOperationException>(() => service.GetPassword(machine));

    var tampered = File.ReadAllText(statePath).Replace("192.0.2.4", "192.0.2.5", StringComparison.Ordinal);
    File.WriteAllText(statePath, tampered);
    Throws<CryptographicException>(() => service.GetPassword(service.GetMachines().Single()));
    File.WriteAllText(statePath, tampered.Replace("192.0.2.5", "192.0.2.4", StringComparison.Ordinal));

    File.Delete(keyPath);
    Throws<CryptographicException>(() => service.Create(null, "192.0.2.1", 22, "user", password));
    Require(service.GetMachines().Single() == edited, "Missing key does not replace the saved store");
    File.WriteAllBytes(keyPath, storedKey);

    Parallel.For(0, 12, index => reloaded.Create($"Machine {index}", "192.0.2.2", 22, "user", password));
    Require(reloaded.GetMachines().Count == 13 && service.Delete(edited.Id) && !reloaded.Delete(edited.Id),
        "Concurrent creates survive and deletion persists");
    Console.WriteLine("PASS: encrypted SSH machine storage, safe updates, and request validation.");
}
finally
{
    directory.Delete(recursive: true);
}

static void Require(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException(description);
}

static void Throws<T>(Action action) where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
