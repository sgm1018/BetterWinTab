using System.Collections.ObjectModel;
using BetterWinTab.Models;
using BetterWinTab.Services;

namespace BetterWinTab.ViewModels;

public sealed class SshWorkspaceViewModel
{
    private IReadOnlyList<SshMachineListItem> _machines = [];
    private string _query = string.Empty;

    public ObservableCollection<SshMachineListItem> Results { get; } = new();
    public bool HasMachines => _machines.Count > 0;

    public void Refresh(SshMachineService service)
    {
        _machines = service.GetMachines()
            .Select(machine => new SshMachineListItem(machine))
            .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Endpoint, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        ApplySearch();
    }

    public void Search(string query)
    {
        _query = query;
        ApplySearch();
    }

    private void ApplySearch()
    {
        var terms = _query.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var matches = _machines.Where(item => terms.All(term => item.Matches(term)));

        Results.Clear();
        foreach (var item in matches)
            Results.Add(item);
    }
}

public sealed class SshMachineListItem
{
    public SshMachineListItem(SshMachine machine)
    {
        Machine = machine;
        Title = string.IsNullOrWhiteSpace(machine.Name) ? machine.Host : machine.Name;
        var displayHost = machine.Host.Contains(':') ? $"[{machine.Host}]" : machine.Host;
        Endpoint = $"{machine.Username}@{displayHost}:{machine.Port}";
    }

    public SshMachine Machine { get; }
    public string Title { get; }
    public string Endpoint { get; }
    public string ConnectLabel => $"Connect to {Title}, {Endpoint}";
    public string OptionsLabel => $"Options for {Title}";

    public bool Matches(string term) =>
        Title.Contains(term, StringComparison.OrdinalIgnoreCase)
        || Endpoint.Contains(term, StringComparison.OrdinalIgnoreCase);
}
