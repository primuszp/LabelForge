using LabelForge.Core;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;

namespace LabelForge.App.AI;

public sealed class Sam3OnnxSegmentor : IDisposable
{
    private const int Resolution = 1008;
    private readonly InferenceSession vision;
    private readonly InferenceSession text;
    private readonly InferenceSession head;
    private readonly ClipTokenizer tokenizer;
    private readonly Dictionary<string, (DenseTensor<float>, DenseTensor<bool>)> textCache = [];
    public bool UsesGpu { get; }

    public Sam3OnnxSegmentor(string modelDirectory)
    {
        RequireFiles(modelDirectory);
        tokenizer = new ClipTokenizer(Path.Combine(modelDirectory, "bpe_merges.txt"));
        var cudaDirectory = Environment.GetEnvironmentVariable("SAM3_CUDA_DLL_DIR")
            ?? @"D:\Projects\VirtualEnvs\torch-venv\Lib\site-packages\torch\lib";
        if (Directory.Exists(cudaDirectory))
            Environment.SetEnvironmentVariable("PATH", cudaDirectory + ";" + Environment.GetEnvironmentVariable("PATH"));
        try
        {
            var gpu = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
            gpu.AppendExecutionProvider_CUDA(0);
            vision = new InferenceSession(Path.Combine(modelDirectory, "sam3_vision_encoder_fp16.onnx"), gpu);
            head = new InferenceSession(Path.Combine(modelDirectory, "sam3_head_fp16.onnx"), gpu);
            UsesGpu = true;
        }
        catch
        {
            vision = new InferenceSession(Path.Combine(modelDirectory, "sam3_vision_encoder.onnx"));
            head = new InferenceSession(Path.Combine(modelDirectory, "sam3_head.onnx"));
        }
        text = new InferenceSession(Path.Combine(modelDirectory, "sam3_text_encoder.onnx"));
    }

    public static void RequireFiles(string directory)
    {
        var required = new[] { "bpe_merges.txt", "sam3_vision_encoder.onnx", "sam3_text_encoder.onnx", "sam3_head.onnx" };
        var missing = required.Where(file => !File.Exists(Path.Combine(directory, file))).ToArray();
        if (missing.Length > 0) throw new FileNotFoundException("Hianyzo SAM3 fajlok: " + string.Join(", ", missing));
    }

    public IReadOnlyList<SegmentationResult> Detect(BitmapSource source, IReadOnlyList<string> prompts,
        float confidenceThreshold = 0.5f, double polygonEpsilon = 2.0)
    {
        var tensor = Preprocess(source);
        using var visionResults = vision.Run([NamedOnnxValue.CreateFromTensor("image", tensor)]);
        var visionInputs = visionResults.Select(result =>
            (result.Name, Tensor: new DenseTensor<float>(result.AsTensor<float>().ToArray(), result.AsTensor<float>().Dimensions.ToArray()))).ToArray();
        var detections = new List<SegmentationResult>();
        for (var promptIndex = 0; promptIndex < prompts.Count; promptIndex++)
        {
            var (features, languageMask) = GetTextFeatures(prompts[promptIndex]);
            var inputs = visionInputs.Select(item => NamedOnnxValue.CreateFromTensor(item.Name, item.Tensor)).ToList();
            inputs.Add(NamedOnnxValue.CreateFromTensor("language_features", features));
            inputs.Add(NamedOnnxValue.CreateFromTensor("language_mask_inv", languageMask));
            using var outputs = head.Run(inputs);
            var scores = outputs.First(result => result.Name == "scores").AsTensor<float>();
            var boxes = outputs.First(result => result.Name == "boxes_xyxy_norm").AsTensor<float>();
            var masks = outputs.First(result => result.Name == "mask_logits").AsTensor<float>();
            for (var query = 0; query < scores.Dimensions[1]; query++)
            {
                var score = scores[0, query];
                if (score <= confidenceThreshold) continue;
                var x0 = Math.Clamp(boxes[0, query, 0] * source.PixelWidth, 0, source.PixelWidth);
                var y0 = Math.Clamp(boxes[0, query, 1] * source.PixelHeight, 0, source.PixelHeight);
                var x1 = Math.Clamp(boxes[0, query, 2] * source.PixelWidth, 0, source.PixelWidth);
                var y1 = Math.Clamp(boxes[0, query, 3] * source.PixelHeight, 0, source.PixelHeight);
                detections.Add(new SegmentationResult
                {
                    X = x0, Y = y0, Width = x1 - x0, Height = y1 - y0,
                    ClassId = promptIndex, Confidence = score,
                    Polygon = MaskToPolygon(masks, query, source.PixelWidth, source.PixelHeight, polygonEpsilon)
                });
            }
        }
        return detections;
    }

    private (DenseTensor<float>, DenseTensor<bool>) GetTextFeatures(string prompt)
    {
        if (textCache.TryGetValue(prompt, out var cached)) return cached;
        var ids = tokenizer.Tokenize(prompt);
        using var outputs = text.Run([NamedOnnxValue.CreateFromTensor("tokens", new DenseTensor<long>(ids, [1, ids.Length]))]);
        var features = outputs.First(result => result.Name == "language_features").AsTensor<float>();
        var mask = outputs.First(result => result.Name == "language_mask_inv").AsTensor<bool>();
        cached = (new DenseTensor<float>(features.ToArray(), features.Dimensions.ToArray()),
            new DenseTensor<bool>(mask.ToArray(), mask.Dimensions.ToArray()));
        textCache[prompt] = cached;
        return cached;
    }

    private static DenseTensor<float> Preprocess(BitmapSource source)
    {
        var rgb = source.Format == PixelFormats.Rgb24 ? source : new FormatConvertedBitmap(source, PixelFormats.Rgb24, null, 0);
        var resized = new TransformedBitmap(rgb, new ScaleTransform((double)Resolution / rgb.PixelWidth, (double)Resolution / rgb.PixelHeight));
        var stride = (Resolution * 3 + 3) & ~3;
        var pixels = new byte[Resolution * stride];
        resized.CopyPixels(pixels, stride, 0);
        var tensor = new DenseTensor<float>([1, 3, Resolution, Resolution]);
        for (var y = 0; y < Resolution; y++) for (var x = 0; x < Resolution; x++)
        {
            var offset = y * stride + x * 3;
            tensor[0, 0, y, x] = pixels[offset] / 127.5f - 1;
            tensor[0, 1, y, x] = pixels[offset + 1] / 127.5f - 1;
            tensor[0, 2, y, x] = pixels[offset + 2] / 127.5f - 1;
        }
        return tensor;
    }

    private static IReadOnlyList<Point2D> MaskToPolygon(Tensor<float> masks, int query, int width, int height, double epsilon)
        => LogitsToPolygon(masks.Dimensions[3], masks.Dimensions[2],
            (x, y) => masks[0, query, y, x], 0, width, height, epsilon);

    internal static IReadOnlyList<Point2D> LogitsToPolygon(int sourceW, int sourceH,
        Func<int, int, float> sample, float threshold, int width, int height, double epsilon)
    {
        const int contourResolution = 2048;
        var scale = Math.Min(1d, contourResolution / (double)Math.Max(width, height));
        var w = Math.Max(sourceW, (int)Math.Round(width * scale));
        var h = Math.Max(sourceH, (int)Math.Round(height * scale));
        var foreground = new bool[h, w];
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
        {
            var sy = (y + 0.5) * sourceH / h - 0.5;
            var sx = (x + 0.5) * sourceW / w - 0.5;
            var y0 = Math.Clamp((int)Math.Floor(sy), 0, sourceH - 1); var y1 = Math.Min(y0 + 1, sourceH - 1);
            var x0 = Math.Clamp((int)Math.Floor(sx), 0, sourceW - 1); var x1 = Math.Min(x0 + 1, sourceW - 1);
            var fy = Math.Clamp(sy - y0, 0, 1); var fx = Math.Clamp(sx - x0, 0, 1);
            var top = sample(x0, y0) * (1 - fx) + sample(x1, y0) * fx;
            var bottom = sample(x0, y1) * (1 - fx) + sample(x1, y1) * fx;
            foreground[y, x] = top * (1 - fy) + bottom * fy > threshold;
        }
        var edges = new Dictionary<(int X, int Y), List<(int X, int Y)>>();
        void Add((int X, int Y) from, (int X, int Y) to)
        {
            if (!edges.TryGetValue(from, out var next)) edges[from] = next = [];
            next.Add(to);
        }
        bool IsSet(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && foreground[y, x];
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
        {
            if (!foreground[y, x]) continue;
            if (!IsSet(x, y - 1)) Add((x, y), (x + 1, y));
            if (!IsSet(x + 1, y)) Add((x + 1, y), (x + 1, y + 1));
            if (!IsSet(x, y + 1)) Add((x + 1, y + 1), (x, y + 1));
            if (!IsSet(x - 1, y)) Add((x, y + 1), (x, y));
        }
        var loops = new List<List<(int X, int Y)>>();
        while (edges.Count > 0)
        {
            var start = edges.First().Key; var current = start; var loop = new List<(int, int)> { start };
            do
            {
                if (!edges.TryGetValue(current, out var next) || next.Count == 0) break;
                var target = next[^1]; next.RemoveAt(next.Count - 1); if (next.Count == 0) edges.Remove(current);
                current = target; loop.Add(current);
            } while (current != start && loop.Count <= w * h * 4);
            if (loop.Count >= 4 && current == start) loops.Add(loop);
        }
        var outline = loops.OrderByDescending(loop => Math.Abs(SignedArea(loop))).FirstOrDefault();
        if (outline is null) return [];
        var points = outline.Select(point => new Point2D(point.X * (double)width / w, point.Y * (double)height / h)).ToList();
        if (points.Count > 1 && points[0] == points[^1]) points.RemoveAt(points.Count - 1);
        var smoothed = SmoothClosed(points, maxDisplacement: 0.75);
        return SimplifyClosed(smoothed, Math.Max(0.65, epsilon));
    }

    private static List<Point2D> SmoothClosed(IReadOnlyList<Point2D> points, double maxDisplacement)
    {
        if (points.Count < 7) return points.ToList();
        var result = new List<Point2D>(points.Count);
        int Wrap(int index) => (index % points.Count + points.Count) % points.Count;
        for (var i = 0; i < points.Count; i++)
        {
            // Symmetric 1-2-3-2-1 kernel removes pixel stairs without shifting the contour globally.
            var x = (points[Wrap(i - 2)].X + 2 * points[Wrap(i - 1)].X + 3 * points[i].X
                + 2 * points[Wrap(i + 1)].X + points[Wrap(i + 2)].X) / 9d;
            var y = (points[Wrap(i - 2)].Y + 2 * points[Wrap(i - 1)].Y + 3 * points[i].Y
                + 2 * points[Wrap(i + 1)].Y + points[Wrap(i + 2)].Y) / 9d;
            var dx = x - points[i].X; var dy = y - points[i].Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance > maxDisplacement)
            {
                var factor = maxDisplacement / distance;
                x = points[i].X + dx * factor;
                y = points[i].Y + dy * factor;
            }
            result.Add(new Point2D(x, y));
        }
        return result;
    }

    private static double SignedArea(IReadOnlyList<(int X, int Y)> points)
    {
        double area = 0;
        for (var i = 0; i < points.Count - 1; i++) area += points[i].X * points[i + 1].Y - points[i + 1].X * points[i].Y;
        return area / 2;
    }

    private static IReadOnlyList<Point2D> SimplifyClosed(List<Point2D> points, double epsilon)
    {
        if (points.Count < 5) return points;
        if (points[0] == points[^1]) points.RemoveAt(points.Count - 1);
        var first = points.MinBy(point => point.X + point.Y)!;
        var start = points.IndexOf(first);
        var rotated = points.Skip(start).Concat(points.Take(start)).ToList();
        rotated.Add(rotated[0]);
        return DouglasPeucker(rotated, epsilon).SkipLast(1).ToArray();
    }

    private static List<Point2D> DouglasPeucker(IReadOnlyList<Point2D> points, double epsilon)
    {
        if (points.Count <= 2) return points.ToList();
        var max = 0d; var index = 0;
        for (var i = 1; i < points.Count - 1; i++)
        {
            var distance = DistanceToLine(points[i], points[0], points[^1]);
            if (distance > max) { max = distance; index = i; }
        }
        if (max <= epsilon) return [points[0], points[^1]];
        var left = DouglasPeucker(points.Take(index + 1).ToArray(), epsilon);
        var right = DouglasPeucker(points.Skip(index).ToArray(), epsilon);
        return [.. left.Take(left.Count - 1), .. right];
    }

    private static double DistanceToLine(Point2D point, Point2D a, Point2D b)
    {
        var dx = b.X - a.X; var dy = b.Y - a.Y;
        if (dx == 0 && dy == 0) return Math.Sqrt(Math.Pow(point.X - a.X, 2) + Math.Pow(point.Y - a.Y, 2));
        return Math.Abs(dy * point.X - dx * point.Y + b.X * a.Y - b.Y * a.X) / Math.Sqrt(dx * dx + dy * dy);
    }

    public void Dispose() { vision.Dispose(); text.Dispose(); head.Dispose(); }
}
