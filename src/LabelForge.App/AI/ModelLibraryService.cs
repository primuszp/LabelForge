using System.IO;

namespace LabelForge.App.AI;

public sealed record ModelLibraryEntry(
    string DisplayName,
    string FilePath,
    YoloKind Kind);

public sealed class ModelLibraryService
{
    public IReadOnlyList<ModelLibraryEntry> GetDownloadedModels()
    {
        Directory.CreateDirectory(PresetModels.ModelsFolder);

        return Directory.EnumerateFiles(PresetModels.ModelsFolder, "*.onnx")
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(CreateEntry)
            .ToList();
    }

    private static ModelLibraryEntry CreateEntry(string path)
    {
        var fileName = Path.GetFileName(path);
        var preset = PresetModels.All.FirstOrDefault(m =>
            string.Equals(m.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        var kind = preset?.Kind ?? InferKind(fileName);
        var displayName = preset is null
            ? $"{fileName} ({KindLabel(kind)})"
            : $"{preset.DisplayName} - {fileName}";

        return new ModelLibraryEntry(displayName, path, kind);
    }

    private static YoloKind InferKind(string fileName)
    {
        if (fileName.Contains("encoder", StringComparison.OrdinalIgnoreCase))
            return YoloKind.SamEncoder;
        if (fileName.Contains("decoder", StringComparison.OrdinalIgnoreCase))
            return YoloKind.SamDecoder;
        if (fileName.Contains("-seg", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("_seg", StringComparison.OrdinalIgnoreCase))
            return YoloKind.Segmentation;

        return YoloKind.Detection;
    }

    private static string KindLabel(YoloKind kind) => kind switch
    {
        YoloKind.Detection => "detection",
        YoloKind.Segmentation => "segmentation",
        YoloKind.SamEncoder => "SAM encoder",
        YoloKind.SamDecoder => "SAM decoder",
        YoloKind.SamTextEncoder => "SAM text",
        YoloKind.SamPackage => "SAM package",
        _ => "model"
    };
}
