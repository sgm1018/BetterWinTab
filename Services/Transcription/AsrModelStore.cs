using System.IO.Compression;
using System.Diagnostics;
using System.Net.Http;
using BetterWinTab.Models.Transcription;

namespace BetterWinTab.Services.Transcription;

/// <summary>Installs a separately obtained sherpa Moonshine archive outside the repository.</summary>
public sealed class AsrModelStore
{
    private readonly string _rootDirectory;
    private readonly string _diarizationDirectory;

    public AsrModelStore(string? rootDirectory = null)
    {
        _rootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterWinTab", "Models", "ASR");
        _diarizationDirectory = rootDirectory == null
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterWinTab", "Models", "Diarization")
            : Path.Combine(rootDirectory, "diarization");
    }

    public string GetModelPath(string modelId) => Path.Combine(_rootDirectory, modelId);
    public string SegmentationModelPath => Path.Combine(_diarizationDirectory, "pyannote-segmentation-3.0", "model.onnx");
    public string EmbeddingModelPath => Path.Combine(_diarizationDirectory, "3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx");
    public bool IsStreamingInstalled => IsInstalled(AsrModelCatalog.Models.Single(model => model.Id == AsrModelCatalog.StreamingModelId)) &&
        File.Exists(SegmentationModelPath) && File.Exists(EmbeddingModelPath);

    public async Task EnsureStreamingModelsAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var model = AsrModelCatalog.Models.Single(item => item.Id == AsrModelCatalog.StreamingModelId);
        if (!IsInstalled(model))
        {
            progress?.Report("Downloading live speech model (454 MB)…");
            await DownloadAndInstallAsync(model, cancellationToken,
                new Progress<double>(value => progress?.Report($"Downloading live speech model… {value:P0}"))).ConfigureAwait(false);
        }
        if (!File.Exists(SegmentationModelPath))
        {
            progress?.Report("Downloading speaker segmentation model…");
            var segmentation = new AsrModel("pyannote-segmentation-3.0", "Speaker segmentation",
                Array.Empty<string>(), AsrExecutionMode.Offline, "See model LICENSE", "Apache-2.0", "7 MB", null,
                new[] { "model.onnx" }, "", true, DiarizationModelCatalog.RequiredAssets[0].ArchiveUri);
            var store = new AsrModelStore(_diarizationDirectory);
            await store.DownloadAndInstallAsync(segmentation, cancellationToken).ConfigureAwait(false);
        }
        if (!File.Exists(EmbeddingModelPath))
        {
            progress?.Report("Downloading speaker voice model (38 MB)…");
            Directory.CreateDirectory(_diarizationDirectory);
            var temporaryPath = EmbeddingModelPath + ".download";
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                using var response = await client.GetAsync(DiarizationModelCatalog.RequiredAssets[1].ArchiveUri,
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var output = File.Create(temporaryPath))
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                if (new FileInfo(temporaryPath).Length < 1024) throw new InvalidDataException("The downloaded speaker model is incomplete.");
                File.Move(temporaryPath, EmbeddingModelPath, overwrite: true);
            }
            finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
        }
        progress?.Report("Speech and speaker models are ready.");
    }

    public bool IsInstalled(AsrModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var path = GetModelPath(model.Id);
        return model.RequiredFiles.Count > 0 && model.RequiredFiles.All(file =>
            File.Exists(Path.Combine(path, file)) && new FileInfo(Path.Combine(path, file)).Length > 0);
    }

    public async Task DownloadAndInstallAsync(AsrModel model, CancellationToken cancellationToken = default,
        IProgress<double>? progress = null)
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
            {
                var buffer = new byte[81920];
                long received = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    received += count;
                    if (response.Content.Headers.ContentLength is > 0)
                        progress?.Report(received / (double)response.Content.Headers.ContentLength.Value);
                }
            }

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
