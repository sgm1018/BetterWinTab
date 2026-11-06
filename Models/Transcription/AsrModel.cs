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
    public const string StreamingModelId = "nemotron-3.5-asr-streaming-0.6b";
    // sherpa-onnx's documented Moonshine v2 Spanish bundle is Base (about 63 MB), offline only.
    // The model is governed by Moonshine Community License; sherpa-onnx itself is Apache-2.0.
    public static IReadOnlyList<AsrModel> Models { get; } = new[]
    {
        new AsrModel(
            StreamingModelId, "Nemotron 3.5 — live transcription", new[] { "es-ES", "es-US", "en-US" },
            AsrExecutionMode.TrueStreaming, "NVIDIA Open Model License", "Apache-2.0",
            "454 MB + speaker models", "sherpa-onnx-nemotron-3.5-asr-streaming-0.6b-560ms-int8-2026-06-11",
            new[] { "encoder.int8.onnx", "decoder.int8.onnx", "joiner.int8.onnx", "tokens.txt" },
            "Local streaming recognition with provisional words and acoustic speaker diarization.", true,
            "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-nemotron-3.5-asr-streaming-0.6b-560ms-int8-2026-06-11.tar.bz2"),
        new AsrModel(
            "moonshine-v2-base-es", "Moonshine v2 Base (Spanish)", new[] { "es" },
            AsrExecutionMode.Offline, "Moonshine Community License", "Apache-2.0",
            "63 MB", "sherpa-onnx-moonshine-base-es-quantized-2026-02-27",
            new[] { "encoder_model.ort", "decoder_model_merged.ort", "tokens.txt", "LICENSE" },
            "Offline file decoding after recording. Model is Spanish only; language detection is not provided. No Tiny/Small Spanish sherpa bundle is documented.", true,
            "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-moonshine-base-es-quantized-2026-02-27.tar.bz2"),
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
        "Voices in system audio are identified locally. Speaker labels may settle as more speech is heard; overlapping voices can remain uncertain.";
}
