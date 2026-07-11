using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Windows.Media.Imaging;

namespace LabelForge.App.AI;

public sealed class YoloV8Detector : IDisposable
{
    private readonly InferenceSession session;

    public YoloV8Detector(string modelPath)
    {
        YoloModelInspector.Require(modelPath, YoloKind.Detection);
        session = CreateSession(modelPath);
    }

    private static InferenceSession CreateSession(string modelPath)
    {
        try
        {
            return new InferenceSession(modelPath);
        }
        catch (Exception ex) when (ex.Message.Contains("opset"))
        {
            throw new InvalidOperationException(
                $"A modell opset verziója nem támogatott.\n\n" +
                $"Exportáld alacsonyabb opset-tel:\n" +
                $"  yolo export model=yolo11n.pt format=onnx opset=17\n\n" +
                $"Eredeti hiba: {ex.Message}", ex);
        }
    }

    public List<DetectionResult> Detect(
        BitmapSource image,
        float confThreshold = 0.50f,
        float nmsThreshold  = 0.30f,
        int   maxDetections = 0)
    {
        var (tensor, scale, padX, padY) = YoloHelper.Preprocess(image);

        var inputName = session.InputMetadata.Keys.First();
        using var outputs = session.Run([NamedOnnxValue.CreateFromTensor(inputName, tensor)]);
        var output = outputs.First().AsTensor<float>();

        var results = PostProcess(output, image.PixelWidth, image.PixelHeight,
                                  scale, padX, padY, confThreshold, nmsThreshold);

        if (maxDetections > 0 && results.Count > maxDetections)
            results = results.OrderByDescending(r => r.Confidence).Take(maxDetections).ToList();

        return results;
    }

    private static List<DetectionResult> PostProcess(
        Tensor<float> output, int imgW, int imgH,
        float scale, int padX, int padY,
        float confThreshold, float nmsThreshold)
    {
        if (output.Dimensions.Length != 3)
            throw new InvalidOperationException("A YOLO predikciós kimenetnek 3 dimenziósnak kell lennie.");
        bool channelsFirst = output.Dimensions[1] < output.Dimensions[2];
        int channels = channelsFirst ? output.Dimensions[1] : output.Dimensions[2];
        int numClasses = channels - 4;
        int numBoxes = channelsFirst ? output.Dimensions[2] : output.Dimensions[1];
        if (numClasses <= 0)
            throw new InvalidOperationException($"Nem támogatott YOLO detection kimenet: [{string.Join("×", output.Dimensions.ToArray())}].");
        float At(int channel, int box) => channelsFirst ? output[0, channel, box] : output[0, box, channel];
        var raw = new List<DetectionResult>(256);

        for (int i = 0; i < numBoxes; i++)
        {
            float maxScore = 0; int bestClass = 0;
            for (int c = 0; c < numClasses; c++)
            {
                float s = At(4 + c, i);
                if (s > maxScore) { maxScore = s; bestClass = c; }
            }
            if (maxScore < confThreshold) continue;

            float cx = At(0, i), cy = At(1, i);
            float w  = At(2, i), h  = At(3, i);

            float x1 = YoloHelper.ToImgX(cx - w / 2, scale, padX, imgW);
            float y1 = YoloHelper.ToImgY(cy - h / 2, scale, padY, imgH);
            float x2 = YoloHelper.ToImgX(cx + w / 2, scale, padX, imgW);
            float y2 = YoloHelper.ToImgY(cy + h / 2, scale, padY, imgH);

            if (x2 - x1 < 2 || y2 - y1 < 2) continue;
            raw.Add(new DetectionResult(x1, y1, x2 - x1, y2 - y1, bestClass, maxScore));
        }

        return YoloHelper.Nms(raw, nmsThreshold,
            r => r.X, r => r.Y, r => r.X + r.Width, r => r.Y + r.Height,
            r => r.Confidence, r => r.ClassId);
    }

    public void Dispose() => session.Dispose();
}
