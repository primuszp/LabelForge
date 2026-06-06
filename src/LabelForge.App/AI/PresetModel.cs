using System.IO;

namespace LabelForge.App.AI;

public enum YoloKind { Detection, Segmentation, SamEncoder, SamDecoder, SamTextEncoder }

public sealed record PresetModel(
    string  DisplayName,
    string  FileName,
    string  DownloadUrl,
    YoloKind Kind,
    string  Description);

public static class PresetModels
{
    private static readonly string Base11 =
        "https://github.com/ultralytics/assets/releases/download/v8.3.0/";

    public static readonly IReadOnlyList<PresetModel> All =
    [
        // ── YOLO11 Detection ──────────────────────────────────────────────
        new("YOLO11n – Detection (nano, ~5 MB)",   "yolo11n.onnx",
            Base11 + "yolo11n.onnx",              YoloKind.Detection,
            "Leggyorsabb, 80 COCO osztály. Ajánlott kezdéshez."),
        new("YOLO11s – Detection (small, ~18 MB)", "yolo11s.onnx",
            Base11 + "yolo11s.onnx",              YoloKind.Detection,
            "Jobb pontosság, még gyors."),
        new("YOLO11m – Detection (medium, ~38 MB)","yolo11m.onnx",
            Base11 + "yolo11m.onnx",              YoloKind.Detection,
            "Kiegyensúlyozott sebesség/pontosság."),
        new("YOLO11l – Detection (large, ~49 MB)", "yolo11l.onnx",
            Base11 + "yolo11l.onnx",              YoloKind.Detection,
            "Nagy pontosság, lassabb."),
        new("YOLO11x – Detection (xlarge, ~109 MB)","yolo11x.onnx",
            Base11 + "yolo11x.onnx",              YoloKind.Detection,
            "Maximális pontosság."),

        // ── YOLO11 Segmentation ───────────────────────────────────────────
        new("YOLO11n – Segmentation (nano, ~5 MB)",  "yolo11n-seg.onnx",
            Base11 + "yolo11n-seg.onnx",             YoloKind.Segmentation,
            "Polygon maszk, leggyorsabb."),
        new("YOLO11s – Segmentation (small, ~19 MB)","yolo11s-seg.onnx",
            Base11 + "yolo11s-seg.onnx",             YoloKind.Segmentation,
            "Polygon maszk, jobb pontosság."),
        new("YOLO11m – Segmentation (medium, ~40 MB)","yolo11m-seg.onnx",
            Base11 + "yolo11m-seg.onnx",             YoloKind.Segmentation,
            "Polygon maszk, kiegyensúlyozott."),

        // ── SAM2.1 Encoder ────────────────────────────────────────────────
        // Export: pip install sam2 && python export_sam2_onnx.py
        // Or download from: https://huggingface.co/Xenova/sam2-hiera-tiny
        new("SAM2.1 Tiny – Encoder (~28 MB)",     "sam2.1_hiera_tiny.encoder.onnx",
            "https://huggingface.co/Xenova/sam2-hiera-tiny/resolve/main/onnx/encoder_model.onnx",
            YoloKind.SamEncoder,
            "SAM2.1 tiny image encoder. Kép → embedding. Egyszer fut képenként."),
        new("SAM2.1 Small – Encoder (~46 MB)",    "sam2.1_hiera_small.encoder.onnx",
            "https://huggingface.co/Xenova/sam2-hiera-small/resolve/main/onnx/encoder_model.onnx",
            YoloKind.SamEncoder,
            "SAM2.1 small image encoder. Jobb minőség."),

        // ── SAM2.1 Decoder ────────────────────────────────────────────────
        new("SAM2.1 Tiny – Decoder (~4 MB)",      "sam2.1_hiera_tiny.decoder.onnx",
            "https://huggingface.co/Xenova/sam2-hiera-tiny/resolve/main/onnx/decoder_model_merged.onnx",
            YoloKind.SamDecoder,
            "SAM2.1 tiny decoder. Kattintás → maszk. Gyors (~50 ms)."),
        new("SAM2.1 Small – Decoder (~4 MB)",     "sam2.1_hiera_small.decoder.onnx",
            "https://huggingface.co/Xenova/sam2-hiera-small/resolve/main/onnx/decoder_model_merged.onnx",
            YoloKind.SamDecoder,
            "SAM2.1 small decoder."),

        // ── SAM3 (Meta, 2025 – 848M params) ──────────────────────────────
        // ONNX export: github.com/facebookresearch/sam3 → export_onnx.py
        // Note: SAM3 is compatible with SAM2 encoder/decoder for click prompts.
        // Text prompts additionally require the text encoder ONNX below.
        new("SAM3 – Image Encoder (~200 MB)",     "sam3_image_encoder.onnx",
            "https://huggingface.co/facebookresearch/sam3/resolve/main/onnx/sam3_image_encoder.onnx",
            YoloKind.SamEncoder,
            "SAM3 vision encoder (848M total params). Kép → embedding."),
        new("SAM3 – Decoder (~10 MB)",            "sam3_decoder.onnx",
            "https://huggingface.co/facebookresearch/sam3/resolve/main/onnx/sam3_decoder.onnx",
            YoloKind.SamDecoder,
            "SAM3 decoder. Kattintás + szöveg → maszk."),
        new("SAM3 – Text Encoder (~80 MB)",       "sam3_text_encoder.onnx",
            "https://huggingface.co/facebookresearch/sam3/resolve/main/onnx/sam3_text_encoder.onnx",
            YoloKind.SamTextEncoder,
            "SAM3 szöveg encoder. Szöveges prompt → szöveg embedding. Szükséges a text prompt funkcióhoz."),

        // ── CLIP vocab (tokenizer) ────────────────────────────────────────
        new("CLIP Vocab (tokenizer vocab.json)",  "vocab.json",
            "https://huggingface.co/openai/clip-vit-base-patch32/resolve/main/vocab.json",
            YoloKind.SamTextEncoder,
            "CLIP szótár a szöveges tokenizáláshoz. Szükséges SAM3 text prompthoz."),
        new("CLIP Merges (tokenizer merges.txt)", "merges.txt",
            "https://huggingface.co/openai/clip-vit-base-patch32/resolve/main/merges.txt",
            YoloKind.SamTextEncoder,
            "CLIP BPE merge fájl a tokenizáláshoz."),
    ];

    /// <summary>
    /// Models folder next to the executable: &lt;app dir&gt;\models\
    /// Falls back to AppData if the app dir is not writable (e.g. Program Files).
    /// Resolved once at first access and cached.
    /// </summary>
    public static string ModelsFolder { get; } = ResolveModelsFolder();

    private static string ResolveModelsFolder()
    {
        var appDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models");
        try
        {
            Directory.CreateDirectory(appDir);
            var test = Path.Combine(appDir, ".write_test");
            File.WriteAllText(test, "ok");
            File.Delete(test);
            return appDir;
        }
        catch
        {
            var fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LabelForge", "models");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }

    public static string LocalPath(PresetModel m) =>
        Path.Combine(ModelsFolder, m.FileName);

    public static bool IsDownloaded(PresetModel m) =>
        File.Exists(LocalPath(m));
}
