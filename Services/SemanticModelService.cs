namespace BetterWinTab.Services;

public sealed class SemanticModelService
{
    public const string ModelName = "multilingual-e5-small";

    private const string ModelUrl = "https://huggingface.co/intfloat/multilingual-e5-small/resolve/main/onnx/model.onnx";
    private const string TokenizerUrl = "https://huggingface.co/intfloat/multilingual-e5-small/resolve/main/sentencepiece.bpe.model";

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _downloadLock = new(1, 1);
    private readonly string _modelDirectory;

    public SemanticModelService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _modelDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BetterWinTab",
            "Models",
            ModelName);
    }

    public string ModelDirectory => _modelDirectory;
    public string ModelPath => Path.Combine(_modelDirectory, "model.onnx");
    public string TokenizerPath => Path.Combine(_modelDirectory, "sentencepiece.bpe.model");
    public bool IsAvailable => File.Exists(ModelPath) && File.Exists(TokenizerPath);

    public async Task<string> EnsureModelAsync(CancellationToken cancellationToken = default)
    {
        if (IsAvailable)
            return _modelDirectory;

        await _downloadLock.WaitAsync(cancellationToken);
        try
        {
            if (IsAvailable)
                return _modelDirectory;

            Directory.CreateDirectory(_modelDirectory);
            await DownloadFileAsync(ModelUrl, ModelPath, cancellationToken);
            await DownloadFileAsync(TokenizerUrl, TokenizerPath, cancellationToken);
            return _modelDirectory;
        }
        catch
        {
            TryDelete(ModelPath);
            TryDelete(TokenizerPath);
            throw;
        }
        finally
        {
            _downloadLock.Release();
        }
    }

    private async Task DownloadFileAsync(string url, string destination, CancellationToken cancellationToken)
    {
        var temporaryPath = destination + ".download";
        TryDelete(temporaryPath);

        using var response = await _httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var target = File.Create(temporaryPath))
        {
            await source.CopyToAsync(target, cancellationToken);
        }

        File.Move(temporaryPath, destination, true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}