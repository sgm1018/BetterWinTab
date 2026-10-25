using System.Text.Json;
using BetterWinTab.Models.Calls;

namespace BetterWinTab.Services.Calls.Ai;

/// <summary>Persists generated reports as individual JSON documents in the user's local app data.</summary>
public sealed class GeneratedDocumentStore
{
    private readonly string _directory;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public GeneratedDocumentStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BetterWinTab", "CallReports");
    }

    public async Task SaveAsync(GeneratedDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"{document.Id:N}.json");
        var temporaryPath = path + ".tmp";
        await using (var stream = File.Create(temporaryPath))
            await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
        File.Move(temporaryPath, path, true);
    }

    public async Task<IReadOnlyList<GeneratedDocument>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_directory))
            return Array.Empty<GeneratedDocument>();
        var documents = new List<GeneratedDocument>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(path);
            var document = await JsonSerializer.DeserializeAsync<GeneratedDocument>(stream, JsonOptions, cancellationToken);
            if (document is not null)
                documents.Add(document);
        }
        return documents.OrderByDescending(d => d.CreatedAtUtc).ToArray();
    }

    public bool Delete(Guid documentId)
    {
        var path = Path.Combine(_directory, $"{documentId:N}.json");
        if (!File.Exists(path))
            return false;
        File.Delete(path);
        return true;
    }
}
