using LabelForge.Core;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Windows.Media.Imaging;

namespace LabelForge.App.AI;

/// <summary>
/// YOLOv8-seg / YOLO11-seg inference.
/// output0: [1, 4+nc+32, 8400]  – boxes + class scores + mask coefficients
/// output1: [1, 32, 160, 160]   – prototype masks
/// </summary>
public sealed class YoloV8Segmentor : IDisposable
{
    private readonly InferenceSession session;
    public YoloV8Segmentor(string modelPath)
    {
        YoloModelInspector.Require(modelPath, YoloKind.Segmentation);
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
                $"  yolo export model=yolo11n-seg.pt format=onnx opset=17\n\n" +
                $"Eredeti hiba: {ex.Message}", ex);
        }
    }

    public List<SegmentationResult> Detect(
        BitmapSource image,
        float confThreshold = 0.50f,
        float nmsThreshold  = 0.30f,
        int   maxDetections = 0,
        float maskThreshold = 0.50f,
        double polygonEpsilon = 2.0)
    {
        var (tensor, scale, padX, padY) = YoloHelper.Preprocess(image);

        var inputName = session.InputMetadata.Keys.First();
        using var outputs = session.Run([NamedOnnxValue.CreateFromTensor(inputName, tensor)]);

        // Exporters may change output names and order. Identify tensors by rank:
        // predictions are rank 3, prototype masks are rank 4.
        var tensors = outputs.Select(output =>
        {
            try { return (output.Name, Tensor: output.AsTensor<float>()); }
            catch (Exception) { return (output.Name, Tensor: (Tensor<float>?)null); }
        }).Where(item => item.Tensor is not null).ToList();

        var output0 = tensors.FirstOrDefault(item => item.Tensor!.Dimensions.Length == 3).Tensor;
        var protos = tensors.FirstOrDefault(item => item.Tensor!.Dimensions.Length == 4).Tensor;
        if (output0 is null || protos is null)
        {
            var actual = string.Join(", ", tensors.Select(item =>
                $"{item.Name}=[{string.Join("×", item.Tensor!.Dimensions.ToArray())}]"));
            throw new InvalidOperationException(
                "A kiválasztott ONNX modell nem kompatibilis YOLO szegmentációs modell. " +
                "A szegmentáláshoz predikciós és prototípus-maszk kimenet is szükséges.\n\n" +
                $"Kapott kimenetek: {(string.IsNullOrEmpty(actual) ? "nincs float tensor" : actual)}\n\n" +
                "Válassz *-seg.onnx modellt, vagy exportáld például így:\n" +
                "yolo export model=yolo11n-seg.pt format=onnx opset=17");
        }

        return PostProcess(output0, protos, image.PixelWidth, image.PixelHeight,
                           scale, padX, padY, confThreshold, nmsThreshold,
                           maxDetections, maskThreshold, polygonEpsilon);
    }

    // ── Post-processing ────────────────────────────────────────────────────

    private static List<SegmentationResult> PostProcess(
        Tensor<float> output0, Tensor<float> protos,
        int imgW, int imgH,
        float scale, int padX, int padY,
        float confThreshold, float nmsThreshold, int maxDetections,
        float maskThreshold, double polygonEpsilon)
    {
        if (output0.Dimensions.Length != 3 || protos.Dimensions.Length != 4)
            throw new InvalidOperationException("Érvénytelen YOLO szegmentációs tensor-dimenziók.");

        int maskCoeffCount = protos.Dimensions[1];
        bool channelsFirst = output0.Dimensions[1] < output0.Dimensions[2];
        int channels = channelsFirst ? output0.Dimensions[1] : output0.Dimensions[2];
        int numClasses = channels - 4 - maskCoeffCount;
        int numProposals = channelsFirst ? output0.Dimensions[2] : output0.Dimensions[1];
        float At(int channel, int proposal) => channelsFirst
            ? output0[0, channel, proposal]
            : output0[0, proposal, channel];
        if (maskCoeffCount <= 0 || numClasses <= 0 || numProposals <= 0)
            throw new InvalidOperationException(
                $"Nem támogatott YOLO szegmentációs kimenet: predictions=[{string.Join("×", output0.Dimensions.ToArray())}], " +
                $"prototypes=[{string.Join("×", protos.Dimensions.ToArray())}].");

        // 1. Collect raw candidates
        var raw = new List<(float x1, float y1, float x2, float y2,
                             int cls, float conf, float[] coeff)>(256);

        for (int i = 0; i < numProposals; i++)
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

            // Mask coefficients
            var coeff = new float[maskCoeffCount];
            int coeffOff = 4 + numClasses;
            for (int k = 0; k < maskCoeffCount; k++)
                coeff[k] = At(coeffOff + k, i);

            raw.Add((x1, y1, x2, y2, bestClass, maxScore, coeff));
        }

        // 2. NMS on bounding boxes
        var kept = YoloHelper.Nms(raw, nmsThreshold,
            r => r.x1, r => r.y1, r => r.x2, r => r.y2,
            r => r.conf, r => r.cls);

        if (maxDetections > 0 && kept.Count > maxDetections)
            kept = kept.OrderByDescending(r => r.conf).Take(maxDetections).ToList();

        // 3. Compute mask polygons for surviving boxes
        var results = new List<SegmentationResult>(kept.Count);
        foreach (var (x1, y1, x2, y2, cls, conf, coeff) in kept)
        {
            var polygon = ComputeMaskPolygon(coeff, protos,
                x1, y1, x2, y2, scale, padX, padY, imgW, imgH,
                maskThreshold, polygonEpsilon);

            results.Add(new SegmentationResult
            {
                X = x1, Y = y1, Width = x2 - x1, Height = y2 - y1,
                ClassId = cls, Confidence = conf,
                Polygon = polygon
            });
        }
        return results;
    }

    // ── Mask → Polygon ────────────────────────────────────────────────────

    private static IReadOnlyList<Point2D> ComputeMaskPolygon(
        float[] coeff, Tensor<float> protos,
        float x1, float y1, float x2, float y2,
        float scale, int padX, int padY, int imgW, int imgH,
        float maskThreshold, double polygonEpsilon)
    {
        // Compute full 160×160 mask in letterboxed prototype space
        var protoH = protos.Dimensions[2];
        var protoW = protos.Dimensions[3];
        var protoScaleX = (float)YoloHelper.InputSize / protoW;
        var protoScaleY = (float)YoloHelper.InputSize / protoH;

        var mask = new bool[protoH, protoW];

        for (int py = 0; py < protoH; py++)
        for (int px = 0; px < protoW; px++)
        {
            float sum = 0;
            for (int k = 0; k < coeff.Length; k++)
                sum += coeff[k] * protos[0, k, py, px];
            mask[py, px] = YoloHelper.Sigmoid(sum) > maskThreshold;
        }

        // Convert prototype pixel → original image coordinate
        // proto pixel (px,py) → letterbox pixel (px*4, py*4) → image ((lx-padX)/scale, (ly-padY)/scale)
        // Bounding box in prototype space (for cropping)
        int bx1 = Math.Clamp((int)Math.Floor((x1 * scale + padX) / protoScaleX), 0, protoW - 1);
        int by1 = Math.Clamp((int)Math.Floor((y1 * scale + padY) / protoScaleY), 0, protoH - 1);
        int bx2 = Math.Clamp((int)Math.Ceiling((x2 * scale + padX) / protoScaleX), 0, protoW - 1);
        int by2 = Math.Clamp((int)Math.Ceiling((y2 * scale + padY) / protoScaleY), 0, protoH - 1);

        // Row-scan: collect left and right outline per row
        var left  = new List<Point2D>(by2 - by1 + 1);
        var right = new List<Point2D>(by2 - by1 + 1);

        for (int py = by1; py <= by2; py++)
        {
            int firstX = -1, lastX = -1;
            for (int px = bx1; px <= bx2; px++)
            {
                if (!mask[py, px]) continue;
                if (firstX < 0) firstX = px;
                lastX = px;
            }
            if (firstX < 0) continue;

            var imgY = ProtoToImageY(py, protoScaleY, scale, padY);
            left.Add(new Point2D(ProtoToImageX(firstX, protoScaleX, scale, padX), imgY));
            right.Add(new Point2D(ProtoToImageX(lastX, protoScaleX, scale, padX), imgY));
        }

        if (left.Count < 3) return [];

        // Polygon: left outline (top→bottom) + right outline (bottom→top)
        var polygon = new List<Point2D>(left.Count + right.Count);
        polygon.AddRange(left);
        for (int i = right.Count - 1; i >= 0; i--)
            polygon.Add(right[i]);

        // Clamp to image bounds
        for (int i = 0; i < polygon.Count; i++)
            polygon[i] = new Point2D(
                Math.Clamp(polygon[i].X, 0, imgW),
                Math.Clamp(polygon[i].Y, 0, imgH));

        // Douglas-Peucker simplification to keep polygon manageable
        return DouglasPeucker(polygon, epsilon: polygonEpsilon);
    }

    private static double ProtoToImageX(int protoX, float protoScaleX, float imageScale, int padX) =>
        ((protoX + 0.5) * protoScaleX - padX) / imageScale;

    private static double ProtoToImageY(int protoY, float protoScaleY, float imageScale, int padY) =>
        ((protoY + 0.5) * protoScaleY - padY) / imageScale;

    // ── Douglas-Peucker ────────────────────────────────────────────────────

    private static List<Point2D> DouglasPeucker(List<Point2D> pts, double epsilon)
    {
        if (pts.Count <= 2) return pts;

        double maxDist = 0; int idx = 0;
        for (int i = 1; i < pts.Count - 1; i++)
        {
            double d = PerpendicularDistance(pts[i], pts[0], pts[^1]);
            if (d > maxDist) { maxDist = d; idx = i; }
        }

        if (maxDist > epsilon)
        {
            var left  = DouglasPeucker(pts[..idx],       epsilon);
            var right = DouglasPeucker(pts[(idx)..(pts.Count)], epsilon);
            return [..left[..^1], ..right];
        }
        return [pts[0], pts[^1]];
    }

    private static double PerpendicularDistance(Point2D p, Point2D a, Point2D b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        if (dx == 0 && dy == 0) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        double len2 = dx * dx + dy * dy;
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        double projX = a.X + t * dx, projY = a.Y + t * dy;
        return Math.Sqrt((p.X - projX) * (p.X - projX) + (p.Y - projY) * (p.Y - projY));
    }

    public void Dispose() => session.Dispose();
}
