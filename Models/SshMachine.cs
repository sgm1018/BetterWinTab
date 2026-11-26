namespace BetterWinTab.Models;

public sealed record SshMachine(string Id, string Name, string Host, int Port, string Username);
