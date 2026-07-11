using System.IO;

namespace LabelForge.App.AI;

public enum YoloKind { Detection, Segmentation }

public sealed record PresetModel(string DisplayName, string FileName, string DownloadUrl,
    YoloKind Kind, string Description);

public static class PresetModels
{
    private const string Base11 = "https://github.com/ultralytics/assets/releases/download/v8.4.0/";

    // Only assets that actually exist in the official release are offered here.
    public static readonly IReadOnlyList<PresetModel> All =
    [
        new("YOLO11n – Detection (nano, ~11 MB)", "yolo11n.onnx", Base11 + "yolo11n.onnx",
            YoloKind.Detection, "Gyors COCO detektálás, 80 osztállyal."),
        new("YOLO11s – Detection (small, ~38 MB)", "yolo11s.onnx", Base11 + "yolo11s.onnx",
            YoloKind.Detection, "Gyors, pontosabb COCO detektálás."),
        new("YOLO11m – Detection (medium, ~81 MB)", "yolo11m.onnx", Base11 + "yolo11m.onnx",
            YoloKind.Detection, "Kiegyensúlyozott sebesség és pontosság."),
        new("YOLO11l – Detection (large, ~102 MB)", "yolo11l.onnx", Base11 + "yolo11l.onnx",
            YoloKind.Detection, "Nagy pontosság, nagyobb gépigény."),
        new("YOLO11x – Detection (xlarge, ~228 MB)", "yolo11x.onnx", Base11 + "yolo11x.onnx",
            YoloKind.Detection, "A legpontosabb, legnagyobb detection modell."),

        new("YOLO11n – Segmentation (nano, ~12 MB)", "yolo11n-seg.onnx", Base11 + "yolo11n-seg.onnx",
            YoloKind.Segmentation, "Gyors COCO instance-szegmentálás polygon maszkkal."),
        new("YOLO11s – Segmentation (small, ~41 MB)", "yolo11s-seg.onnx", Base11 + "yolo11s-seg.onnx",
            YoloKind.Segmentation, "Pontosabb instance-szegmentálás."),
        new("YOLO11m – Segmentation (medium, ~90 MB)", "yolo11m-seg.onnx", Base11 + "yolo11m-seg.onnx",
            YoloKind.Segmentation, "Kiegyensúlyozott segmentation modell."),
        new("YOLO11l – Segmentation (large, ~111 MB)", "yolo11l-seg.onnx", Base11 + "yolo11l-seg.onnx",
            YoloKind.Segmentation, "Nagy pontosságú instance-szegmentálás."),
        new("YOLO11x – Segmentation (xlarge, ~249 MB)", "yolo11x-seg.onnx", Base11 + "yolo11x-seg.onnx",
            YoloKind.Segmentation, "A legpontosabb, legnagyobb segmentation modell.")
    ];

    public static string ModelsFolder { get; } = ResolveModelsFolder();

    private static string ResolveModelsFolder()
    {
        var appDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models");
        try
        {
            Directory.CreateDirectory(appDir);
            var test = Path.Combine(appDir, ".write_test");
            File.WriteAllText(test, "ok"); File.Delete(test);
            return appDir;
        }
        catch
        {
            var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LabelForge", "models");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }

    public static string LocalPath(PresetModel model) => Path.Combine(ModelsFolder, model.FileName);
    public static string PackageFolder(PresetModel model) => Path.Combine(ModelsFolder, Path.GetFileNameWithoutExtension(model.FileName));
    public static bool IsDownloaded(PresetModel model) => File.Exists(LocalPath(model));
}
