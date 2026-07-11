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
        var inspection = YoloModelInspector.Inspect(path);
        var kind = inspection.IsCompatible ? inspection.Kind : preset?.Kind ?? InferKind(fileName);
        var displayName = preset is null
            ? $"{fileName} ({(inspection.IsCompatible ? KindLabel(kind) : "invalid")})"
            : $"{preset.DisplayName} - {fileName}{(inspection.IsCompatible ? string.Empty : " [invalid]")}";

        return new ModelLibraryEntry(displayName, path, kind);
    }

    private static YoloKind InferKind(string fileName)
    {
        if (fileName.Contains("-seg", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("_seg", StringComparison.OrdinalIgnoreCase))
            return YoloKind.Segmentation;

        return YoloKind.Detection;
    }

    private static string KindLabel(YoloKind kind) => kind switch
    {
        YoloKind.Detection => "detection",
        YoloKind.Segmentation => "segmentation",
        _ => "model"
    };
}
