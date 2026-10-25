namespace BetterWinTab.Models.Transcription;

public enum AsrExecutionMode
{
    Offline,
    SimulatedStreaming,
    TrueStreaming
}

public sealed record AsrModel(
    string Id,
    string DisplayName,
    IReadOnlyList<string> Locales,
    AsrExecutionMode ExecutionMode,
    string ModelLicense,
    string RuntimeLicense,
    string ApproximateDownloadSize,
    string? ModelDirectoryName,
    IReadOnlyList<string> RequiredFiles,
    string Notes,
    bool RuntimeAvailable,
    string? ArchiveUri = null);

public sealed class AsrModelCatalog
{
    // sherpa-onnx's documented Moonshine v2 Spanish bundle is Base (about 63 MB), offline only.
    // The model is governed by Moonshine Community License; sherpa-onnx itself is Apache-2.0.
    public static IReadOnlyList<AsrModel> Models { get; } = new[]
    {
        new AsrModel(
            "moonshine-v2-base-es", "Moonshine v2 Base (Spanish)", new[] { "es" },
            AsrExecutionMode.Offline, "Moonshine Community License", "Apache-2.0",
            "63 MB", "sherpa-onnx-moonshine-base-es-quantized-2026-02-27",
            new[] { "encoder_model.ort", "decoder_model_merged.ort", "tokens.txt", "LICENSE" },
            "Offline file decoding after recording. Model is Spanish only; language detection is not provided. No Tiny/Small Spanish sherpa bundle is documented.", true,
            "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-moonshine-base-es-quantized-2026-02-27.tar.bz2"),
        new AsrModel(
            "nemotron-3.5-asr-streaming-0.6b", "Nemotron 3.5 ASR Streaming 0.6B", new[] { "es-ES", "es-US" },
            AsrExecutionMode.TrueStreaming, "NVIDIA Open Model License", "Apache-2.0",
            "model bundle size depends on exported quantization", null, Array.Empty<string>(),
            "Sherpa-onnx documents streaming support; substantially larger (600M parameters) and CPU latency/memory must be measured on target hardware.", false)
    };
}

public sealed record DiarizationModelAsset(string Id, string DisplayName, string ArchiveUri, string ModelLicense, string Notes);

public static class DiarizationModelCatalog
{
    public static IReadOnlyList<DiarizationModelAsset> RequiredAssets { get; } = new[]
    {
        new DiarizationModelAsset("pyannote-segmentation-3.0", "Pyannote segmentation 3.0",
            "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-segmentation-models/sherpa-onnx-pyannote-segmentation-3-0.tar.bz2",
            "See the model archive LICENSE; upstream is converted from pyannote/segmentation-3.0.", "Speaker activity segmentation model."),
        new DiarizationModelAsset("3dspeaker-eres2net", "3D-Speaker ERes2Net embedding",
            "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx",
            "See the upstream 3D-Speaker model terms before redistribution.", "Speaker embedding model; official sherpa examples use it with pyannote segmentation.")
    };

    public const string CurrentTranscriptCapability =
        "Current labels distinguish only microphone from call audio. Remote-speaker diarization would require the Pyannote segmentation and 3D-Speaker embedding models listed here, plus timestamp alignment; Moonshine Base returns no word timestamps, so speakers are not assigned to transcript text.";
}
