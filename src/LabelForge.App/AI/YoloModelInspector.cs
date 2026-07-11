using Microsoft.ML.OnnxRuntime;

namespace LabelForge.App.AI;

public sealed record YoloModelInfo(YoloKind Kind, string InputName, int[] InputShape,
    IReadOnlyList<string> Outputs, bool IsCompatible, string Message);

public static class YoloModelInspector
{
    public static YoloModelInfo Inspect(string modelPath)
    {
        try
        {
            using var session = new InferenceSession(modelPath);
            var input = session.InputMetadata.FirstOrDefault();
            if (string.IsNullOrEmpty(input.Key) || input.Value.Dimensions.Length != 4)
                return Invalid("A modellnek egy négydimenziós képtensor bemenetre van szüksége.");

            var outputs = session.OutputMetadata
                .Select(item => $"{item.Key}=[{string.Join("×", item.Value.Dimensions)}]").ToList();
            var hasPredictions = session.OutputMetadata.Any(item => item.Value.Dimensions.Length == 3);
            var hasPrototypes = session.OutputMetadata.Any(item => item.Value.Dimensions.Length == 4);
            if (!hasPredictions)
                return Invalid($"Nem található YOLO predikciós kimenet. Kimenetek: {string.Join(", ", outputs)}");

            var kind = hasPrototypes ? YoloKind.Segmentation : YoloKind.Detection;
            return new YoloModelInfo(kind, input.Key, input.Value.Dimensions, outputs, true,
                kind == YoloKind.Segmentation ? "Kompatibilis YOLO szegmentációs modell." : "Kompatibilis YOLO detekciós modell.");
        }
        catch (Exception ex) { return Invalid($"Az ONNX modell nem nyitható meg: {ex.Message}"); }
    }

    public static YoloModelInfo Require(string modelPath, YoloKind expected)
    {
        var info = Inspect(modelPath);
        if (!info.IsCompatible) throw new InvalidOperationException(info.Message);
        if (info.Kind != expected)
            throw new InvalidOperationException($"A modell típusa {Name(info.Kind)}, a kiválasztott művelet viszont {Name(expected)}.");
        return info;
    }

    private static YoloModelInfo Invalid(string message) => new(YoloKind.Detection, string.Empty, [], [], false, message);
    private static string Name(YoloKind kind) => kind == YoloKind.Segmentation ? "szegmentáció" : "detektálás";
}
