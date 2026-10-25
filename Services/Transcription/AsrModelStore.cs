using System.IO.Compression;
using System.Diagnostics;
using System.Net.Http;
using BetterWinTab.Models.Transcription;

namespace BetterWinTab.Services.Transcription;

/// <summary>Installs a separately obtained sherpa Moonshine archive outside the repository.</summary>
public sealed class AsrModelStore
{
    private readonly string _rootDirectory;

    public AsrModelStore(string? rootDirectory = null)
    {
        _rootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterWinTab", "Models", "ASR");
    }

    public string GetModelPath(string modelId) => Path.Combine(_rootDirectory, modelId);

    public bool IsInstalled(AsrModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var path = GetModelPath(model.Id);
        return model.RequiredFiles.Count > 0 && model.RequiredFiles.All(file => File.Exists(Path.Combine(path, file)));
    }

    public async Task DownloadAndInstallAsync(AsrModel model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!Uri.TryCreate(model.ArchiveUri, UriKind.Absolute, out var archiveUri) ||
            archiveUri.Scheme != Uri.UriSchemeHttps)
            throw new NotSupportedException("This model does not have a verified HTTPS download source.");

        var archivePath = Path.Combine(Path.GetTempPath(), $"betterwintab-{model.Id}-{Guid.NewGuid():N}.tar.bz2");
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            using var response = await client.GetAsync(archiveUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             bufferSize: 81920, useAsync: true))
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);

            await InstallArchiveAsync(model, archivePath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(archivePath); } catch (IOException) { }
        }
    }

    /// <summary>Imports a user-provided ZIP or official .tar.bz2 model archive into local app data.</summary>
    public async Task InstallArchiveAsync(AsrModel model, string archivePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        if (!File.Exists(archivePath)) throw new FileNotFoundException("ASR model archive not found.", archivePath);
        if (model.RequiredFiles.Count == 0) throw new NotSupportedException("This model has no verified sherpa archive layout in the catalog.");

        var staging = GetModelPath(model.Id) + ".installing";
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);
        try
        {
            if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var archive = ZipFile.OpenRead(archivePath);
                var root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var destination = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                    if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The model archive contains an unsafe path.");
                    if (entry.FullName.EndsWith('/'))
                    {
                        Directory.CreateDirectory(destination);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await using var source = entry.Open();
                    await using var target = File.Create(destination);
                    await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                }
            }
            else if (archivePath.EndsWith(".tar.bz2", StringComparison.OrdinalIgnoreCase))
            {
                var start = new ProcessStartInfo("tar.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };
                start.ArgumentList.Add("-xjf");
                start.ArgumentList.Add(Path.GetFullPath(archivePath));
                start.ArgumentList.Add("-C");
                start.ArgumentList.Add(Path.GetFullPath(staging));
                using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Windows tar.exe.");
                using var registration = cancellationToken.Register(() => { try { process.Kill(entireProcessTree: true); } catch { } });
                var error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                if (process.ExitCode != 0)
                    throw new InvalidDataException($"Model archive extraction failed: {error}");
            }
            else
            {
                throw new InvalidDataException("Choose a .zip or .tar.bz2 model archive.");
            }

            var modelRoot = FindRequiredFilesRoot(staging, model.RequiredFiles);
            var destinationRoot = GetModelPath(model.Id);
            Directory.CreateDirectory(_rootDirectory);
            var finalStaging = destinationRoot + ".new";
            if (Directory.Exists(finalStaging)) Directory.Delete(finalStaging, recursive: true);
            if (string.Equals(Path.GetFullPath(modelRoot), Path.GetFullPath(staging), StringComparison.OrdinalIgnoreCase))
                Directory.Move(staging, finalStaging);
            else
                Directory.Move(modelRoot, finalStaging);
            if (Directory.Exists(destinationRoot)) Directory.Delete(destinationRoot, recursive: true);
            Directory.Move(finalStaging, destinationRoot);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static string FindRequiredFilesRoot(string staging, IReadOnlyList<string> requiredFiles)
    {
        var candidates = Directory.GetFiles(staging, requiredFiles[0], SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName).Where(path => path is not null).Cast<string>();
        return candidates.FirstOrDefault(path => requiredFiles.All(file => File.Exists(Path.Combine(path, file))))
            ?? throw new InvalidDataException("Archive is missing one or more required model files.");
    }
}
