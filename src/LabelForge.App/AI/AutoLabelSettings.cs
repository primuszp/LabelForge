using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LabelForge.App.AI;

/// <summary>
/// Application-wide singleton for AI auto-label settings.
/// Shared between AutoLabelDialog and the toolbar quick-run buttons.
/// </summary>
public static class AutoLabelSettings
{
    // ── YOLO model paths ──────────────────────────────────────────────────
    public static string DetectionModelPath    { get; set; } = string.Empty;
    public static string SegmentationModelPath { get; set; } = string.Empty;
    public static string SegmentationProvider { get; set; } = "YOLO";
    public static string Sam3ModelDirectory { get; set; } = @"D:\Projects\SAM3\onnx_export";
    public static string Sam3Prompts { get; set; } = "car,truck,person";

    // ── Shared inference parameters ───────────────────────────────────────
    public static float  Confidence  { get; set; } = 0.50f;
    public static float  Nms         { get; set; } = 0.30f;
    public static int    MaxDet      { get; set; } = 0;
    public static float  SegmentationMaskThreshold { get; set; } = 0.50f;
    public static double SegmentationPolygonEpsilon { get; set; } = 0.20;
    public static bool   CurrentOnly { get; set; } = true;
    private static readonly string[] CocoClassNames =
    [
        "person", "bicycle", "car", "motorcycle", "airplane", "bus", "train", "truck", "boat", "traffic light",
        "fire hydrant", "stop sign", "parking meter", "bench", "bird", "cat", "dog", "horse", "sheep", "cow",
        "elephant", "bear", "zebra", "giraffe", "backpack", "umbrella", "handbag", "tie", "suitcase", "frisbee",
        "skis", "snowboard", "sports ball", "kite", "baseball bat", "baseball glove", "skateboard", "surfboard",
        "tennis racket", "bottle", "wine glass", "cup", "fork", "knife", "spoon", "bowl", "banana", "apple",
        "sandwich", "orange", "broccoli", "carrot", "hot dog", "pizza", "donut", "cake", "chair", "couch",
        "potted plant", "bed", "dining table", "toilet", "tv", "laptop", "mouse", "remote", "keyboard",
        "cell phone", "microwave", "oven", "toaster", "sink", "refrigerator", "book", "clock", "vase", "scissors",
        "teddy bear", "hair drier", "toothbrush"
    ];

    public static string ClassNames { get; set; } = string.Join('\n', CocoClassNames);

    // ── Convenience ───────────────────────────────────────────────────────
    public static bool DetectionReady    => File.Exists(DetectionModelPath);
    public static bool SegmentationReady => SegmentationProvider == "SAM3"
        ? Directory.Exists(Sam3ModelDirectory) && !string.IsNullOrWhiteSpace(Sam3Prompts)
        : File.Exists(SegmentationModelPath);

    public static IReadOnlyList<string> ClassNameList =>
        ClassNames
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    public static async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var path = SettingsFilePath;
        if (!File.Exists(path)) return;

        await using var stream = File.OpenRead(path);
        var dto = await JsonSerializer.DeserializeAsync<AutoLabelSettingsDto>(
            stream, JsonOptions, cancellationToken);
        if (dto is null) return;

        DetectionModelPath = dto.DetectionModelPath ?? string.Empty;
        SegmentationModelPath = dto.SegmentationModelPath ?? string.Empty;
        Confidence = dto.Confidence;
        Nms = dto.Nms;
        MaxDet = dto.MaxDet;
        SegmentationMaskThreshold = dto.SegmentationMaskThreshold;
        SegmentationPolygonEpsilon = dto.SegmentationPolygonEpsilon;
        CurrentOnly = dto.CurrentOnly;
        ClassNames = string.IsNullOrWhiteSpace(dto.ClassNames) ? ClassNames : dto.ClassNames;
        SegmentationProvider = dto.SegmentationProvider ?? "YOLO";
        Sam3ModelDirectory = dto.Sam3ModelDirectory ?? Sam3ModelDirectory;
        Sam3Prompts = dto.Sam3Prompts ?? Sam3Prompts;
        RepairYoloModelAssignments();
        RepairPresetClassNames();
    }

    public static async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(SettingsFolder);
        var dto = new AutoLabelSettingsDto
        {
            DetectionModelPath = DetectionModelPath,
            SegmentationModelPath = SegmentationModelPath,
            Confidence = Confidence,
            Nms = Nms,
            MaxDet = MaxDet,
            SegmentationMaskThreshold = SegmentationMaskThreshold,
            SegmentationPolygonEpsilon = SegmentationPolygonEpsilon,
            CurrentOnly = CurrentOnly,
            ClassNames = ClassNames
            ,SegmentationProvider = SegmentationProvider
            ,Sam3ModelDirectory = Sam3ModelDirectory
            ,Sam3Prompts = Sam3Prompts
        };

        await using var stream = File.Create(SettingsFilePath);
        await JsonSerializer.SerializeAsync(stream, dto, JsonOptions, cancellationToken);
    }

    private static void RepairYoloModelAssignments()
    {
        if (!File.Exists(SegmentationModelPath)) return;
        var info = YoloModelInspector.Inspect(SegmentationModelPath);
        if (!info.IsCompatible || info.Kind == YoloKind.Segmentation) return;
        var directory = Path.GetDirectoryName(SegmentationModelPath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(SegmentationModelPath);
        var candidates = new[]
        {
            Path.Combine(directory, baseName + "-seg.onnx"),
            Path.Combine(directory, baseName + "_seg.onnx")
        };
        var replacement = candidates.FirstOrDefault(path => File.Exists(path)
            && YoloModelInspector.Inspect(path) is { IsCompatible: true, Kind: YoloKind.Segmentation });
        if (replacement is not null) SegmentationModelPath = replacement;
    }

    private static void RepairPresetClassNames()
    {
        var names = new[] { DetectionModelPath, SegmentationModelPath }.Where(File.Exists)
            .Select(Path.GetFileName).Where(name => name is not null).Cast<string>();
        var isCocoPreset = names.Any(name => Regex.IsMatch(name, @"^yolo(?:8|11)[nslmx](?:-seg)?\.onnx$", RegexOptions.IgnoreCase));
        if (isCocoPreset && ClassNameList.Count < CocoClassNames.Length)
            ClassNames = string.Join('\n', CocoClassNames);
    }

    private static string SettingsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LabelForge");

    private static string SettingsFilePath => Path.Combine(SettingsFolder, "ai-settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed class AutoLabelSettingsDto
    {
        public string? DetectionModelPath { get; set; }
        public string? SegmentationModelPath { get; set; }
        public float Confidence { get; set; } = 0.50f;
        public float Nms { get; set; } = 0.30f;
        public int MaxDet { get; set; }
        public float SegmentationMaskThreshold { get; set; } = 0.50f;
        public double SegmentationPolygonEpsilon { get; set; } = 0.20;
        public bool CurrentOnly { get; set; } = true;
        public string? ClassNames { get; set; }
        public string? SegmentationProvider { get; set; }
        public string? Sam3ModelDirectory { get; set; }
        public string? Sam3Prompts { get; set; }
    }
}
