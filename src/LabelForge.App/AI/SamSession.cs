using System.IO;
using LabelForge.Core;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LabelForge.App.AI;

/// <summary>
/// SAM2.1 two-model pipeline.
/// Encoder: image → embedding  (run once per image, cached)
/// Decoder: embedding + click points → mask  (run per interaction, ~50 ms)
///
/// Compatible ONNX models can be exported with:
///   pip install sam2
///   python -c "from sam2.build_sam import build_sam2; ..."
///
/// Or download from HuggingFace:
///   https://huggingface.co/Xenova/sam2-hiera-tiny/tree/main/onnx
/// </summary>
public sealed class SamSession : IDisposable
{
    private readonly InferenceSession encoder;
    private readonly InferenceSession decoder;
    private readonly InferenceSession? textEncoder;  // SAM3 only, optional

    // ── Cached embedding per image ────────────────────────────────────────
    private float[]? imageEmbed;
    private float[]? highResFeats0;
    private float[]? highResFeats1;
    private int cachedImgW, cachedImgH;
    private float cachedScale;
    private int cachedPadX, cachedPadY;

    public bool HasEmbedding => imageEmbed is not null;

    public bool HasTextEncoder => textEncoder is not null;

    public SamSession(string encoderPath, string decoderPath,
        string? textEncoderPath = null)
    {
        encoder = new InferenceSession(encoderPath);
        decoder = new InferenceSession(decoderPath);
        if (!string.IsNullOrEmpty(textEncoderPath) && File.Exists(textEncoderPath))
            textEncoder = new InferenceSession(textEncoderPath);
    }

    // ── Encode (run once per image) ───────────────────────────────────────

    public async Task EncodeAsync(BitmapSource src, CancellationToken ct = default)
    {
        await Task.Run(() => EncodeCore(src), ct);
    }

    private void EncodeCore(BitmapSource src)
    {
        const int InputSize = 1024;

        float scale = Math.Min((float)InputSize / src.PixelWidth,
                               (float)InputSize / src.PixelHeight);

        // Resize to target
        var srcRgb   = src.Format != PixelFormats.Rgb24
            ? (BitmapSource)new FormatConvertedBitmap(src, PixelFormats.Rgb24, null, 0)
            : src;
        var resized  = new TransformedBitmap(srcRgb, new ScaleTransform(scale, scale));
        var rgb      = new FormatConvertedBitmap(resized, PixelFormats.Rgb24, null, 0);

        int actualW = rgb.PixelWidth;
        int actualH = rgb.PixelHeight;
        int padX    = (InputSize - actualW) / 2;
        int padY    = (InputSize - actualH) / 2;

        int stride  = (actualW * 3 + 3) & ~3;
        var pixels  = new byte[actualH * stride];
        rgb.CopyPixels(pixels, stride, 0);

        // SAM2 uses ImageNet normalisation (not 0-1)
        // mean = [0.485, 0.456, 0.406], std = [0.229, 0.224, 0.225]
        float[] mean = [0.485f, 0.456f, 0.406f];
        float[] std  = [0.229f, 0.224f, 0.225f];
        float pad    = 0f;  // gray pad value (normalised mean)

        var tensor = new DenseTensor<float>(new[] { 1, 3, InputSize, InputSize });

        // Init padding value (channel-specific mean)
        for (int c = 0; c < 3; c++)
            for (int y = 0; y < InputSize; y++)
                for (int x = 0; x < InputSize; x++)
                    tensor[0, c, y, x] = (pad - mean[c]) / std[c];

        for (int y = 0; y < actualH; y++)
        for (int x = 0; x < actualW; x++)
        {
            int i = y * stride + x * 3;
            tensor[0, 0, y + padY, x + padX] = (pixels[i]     / 255f - mean[0]) / std[0]; // R
            tensor[0, 1, y + padY, x + padX] = (pixels[i + 1] / 255f - mean[1]) / std[1]; // G
            tensor[0, 2, y + padY, x + padX] = (pixels[i + 2] / 255f - mean[2]) / std[2]; // B
        }

        // Run encoder
        var inputName = encoder.InputMetadata.Keys.First();
        using var outputs = encoder.Run([NamedOnnxValue.CreateFromTensor(inputName, tensor)]);
        var outList = outputs.ToList();

        imageEmbed = ToArray(outList, "image_embed")
                  ?? ToArrayByShape(outList, [1, 256, 64, 64])
                  ?? ToArrayByIndex(outList, 0);
        highResFeats0 = ToArray(outList, "high_res_feats_0")
                     ?? ToArrayByShape(outList, [1, 32, 256, 256]);
        highResFeats1 = ToArray(outList, "high_res_feats_1")
                     ?? ToArrayByShape(outList, [1, 64, 128, 128]);

        cachedImgW  = src.PixelWidth;
        cachedImgH  = src.PixelHeight;
        cachedScale = scale;
        cachedPadX  = padX;
        cachedPadY  = padY;
    }

    // ── Decode (per click) ────────────────────────────────────────────────

    /// <summary>
    /// Returns a boolean mask at image resolution, or null on failure.
    /// Points are in original image pixel coordinates.
    /// isForeground=true = foreground prompt, false = background/exclusion.
    /// </summary>
    public async Task<bool[,]?> DecodeAsync(
        IReadOnlyList<(Point2D point, bool isForeground)> points,
        string? textPrompt = null,
        CancellationToken ct = default)
    {
        if (!HasEmbedding) return null;
        if (points.Count == 0 && string.IsNullOrWhiteSpace(textPrompt)) return null;

        float[]? textEmbed = null;
        if (!string.IsNullOrWhiteSpace(textPrompt) && textEncoder is not null)
            textEmbed = await Task.Run(() => EncodeText(textPrompt!), ct);

        return await Task.Run(() => DecodeCore(points, textEmbed), ct);
    }

    // ── Text encoding (SAM3) ──────────────────────────────────────────────

    private float[]? EncodeText(string text)
    {
        if (textEncoder is null) return null;

        var tokens = ClipTokenizer.Tokenize(text);
        var tokenTensor = new DenseTensor<long>(new[] { 1, 77 });
        for (int i = 0; i < 77; i++)
            tokenTensor[0, i] = i < tokens.Length ? tokens[i] : 0L;

        var inputName = textEncoder.InputMetadata.Keys.First();
        using var outputs = textEncoder.Run(
            [NamedOnnxValue.CreateFromTensor(inputName, tokenTensor)]);

        return TensorToArray(outputs.First().AsTensor<float>());
    }

    private bool[,]? DecodeCore(IReadOnlyList<(Point2D pt, bool fg)> points,
        float[]? textEmbed = null)
    {
        if (imageEmbed is null) return null;

        const int InputSize = 1024;
        int n = points.Count;

        // point_coords [1, N, 2]  – in 1024×1024 letterboxed space
        // point_labels [1, N]     – 1=fg, 0=bg
        var coords = new DenseTensor<float>(new[] { 1, n, 2 });
        var labels = new DenseTensor<float>(new[] { 1, n });

        for (int i = 0; i < n; i++)
        {
            var (pt, fg) = points[i];
            coords[0, i, 0] = (float)(pt.X * cachedScale + cachedPadX);
            coords[0, i, 1] = (float)(pt.Y * cachedScale + cachedPadY);
            labels[0, i]    = fg ? 1f : 0f;
        }

        // mask_input [1,1,256,256] – zeros (no previous mask)
        var maskInput    = new DenseTensor<float>(new[] { 1, 1, 256, 256 });
        var hasMaskInput = new DenseTensor<float>(new[] { 1 });
        hasMaskInput[0] = 0f;

        // Rebuild embedding tensors
        var embedShape  = new[] { 1, 256, 64, 64 };
        var hrf0Shape   = new[] { 1, 32, 256, 256 };
        var hrf1Shape   = new[] { 1, 64, 128, 128 };

        var embedTensor = new DenseTensor<float>(imageEmbed!, embedShape);
        var hrf0Tensor  = highResFeats0 is not null
            ? new DenseTensor<float>(highResFeats0, hrf0Shape) : null;
        var hrf1Tensor  = highResFeats1 is not null
            ? new DenseTensor<float>(highResFeats1, hrf1Shape) : null;

        if (decoder.InputMetadata.ContainsKey("high_res_feats_0") && hrf0Tensor is null)
            throw new InvalidOperationException("A SAM decoder high_res_feats_0 inputot vár, de az encoder outputból nem sikerült kiolvasni.");
        if (decoder.InputMetadata.ContainsKey("high_res_feats_1") && hrf1Tensor is null)
            throw new InvalidOperationException("A SAM decoder high_res_feats_1 inputot vár, de az encoder outputból nem sikerült kiolvasni.");

        // Build named inputs (handle models that may omit high_res_feats)
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("image_embed",    embedTensor),
            NamedOnnxValue.CreateFromTensor("point_coords",   coords),
            NamedOnnxValue.CreateFromTensor("point_labels",   labels),
            NamedOnnxValue.CreateFromTensor("mask_input",     maskInput),
            NamedOnnxValue.CreateFromTensor("has_mask_input", hasMaskInput),
        };

        if (hrf0Tensor is not null)
            inputs.Insert(1, NamedOnnxValue.CreateFromTensor("high_res_feats_0", hrf0Tensor));
        if (hrf1Tensor is not null)
            inputs.Insert(2, NamedOnnxValue.CreateFromTensor("high_res_feats_1", hrf1Tensor));

        // SAM3 text embedding (optional)
        if (textEmbed is not null && decoder.InputMetadata.ContainsKey("text_embed"))
        {
            var textShape = new[] { 1, textEmbed.Length };
            inputs.Add(NamedOnnxValue.CreateFromTensor("text_embed",
                new DenseTensor<float>(textEmbed, textShape)));
        }

        // Only pass inputs the model actually expects
        var modelInputNames = decoder.InputMetadata.Keys.ToHashSet();
        inputs = inputs.Where(i => modelInputNames.Contains(i.Name)).ToList();

        using var outputs = decoder.Run(inputs);
        var masks = outputs.First().AsTensor<float>();

        // Convert logits mask [1,1,H,W] → bool[imgH, imgW] in image coords
        int mH = masks.Dimensions[2];
        int mW = masks.Dimensions[3];

        var result = new bool[cachedImgH, cachedImgW];

        for (int my = 0; my < mH; my++)
        for (int mx = 0; mx < mW; mx++)
        {
            if (masks[0, 0, my, mx] <= 0) continue;

            // Map from mask space → letterbox space → image space
            float lx = mx * ((float)InputSize / mW);
            float ly = my * ((float)InputSize / mH);
            int ix = (int)((lx - cachedPadX) / cachedScale);
            int iy = (int)((ly - cachedPadY) / cachedScale);

            if (ix >= 0 && ix < cachedImgW && iy >= 0 && iy < cachedImgH)
                result[iy, ix] = true;
        }

        return result;
    }

    // ── Polygon extraction ────────────────────────────────────────────────

    public static IReadOnlyList<Point2D> MaskToPolygon(bool[,] mask, int imgW, int imgH)
    {
        var left  = new List<Point2D>(imgH);
        var right = new List<Point2D>(imgH);

        for (int y = 0; y < imgH; y++)
        {
            int f = -1, l = -1;
            for (int x = 0; x < imgW; x++)
            {
                if (!mask[y, x]) continue;
                if (f < 0) f = x;
                l = x;
            }
            if (f < 0) continue;
            left.Add(new Point2D(f, y));
            right.Add(new Point2D(l, y));
        }

        if (left.Count < 3) return [];

        var polygon = new List<Point2D>(left.Count + right.Count);
        polygon.AddRange(left);
        for (int i = right.Count - 1; i >= 0; i--) polygon.Add(right[i]);

        return DouglasPeucker(polygon, epsilon: 2.0);
    }

    private static List<Point2D> DouglasPeucker(List<Point2D> pts, double epsilon)
    {
        if (pts.Count <= 2) return pts;
        double maxD = 0; int idx = 0;
        for (int i = 1; i < pts.Count - 1; i++)
        {
            double d = PerpDist(pts[i], pts[0], pts[^1]);
            if (d > maxD) { maxD = d; idx = i; }
        }
        if (maxD > epsilon)
        {
            var l = DouglasPeucker(pts[..idx], epsilon);
            var r = DouglasPeucker(pts[idx..], epsilon);
            return [..l[..^1], ..r];
        }
        return [pts[0], pts[^1]];
    }

    private static double PerpDist(Point2D p, Point2D a, Point2D b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        if (dx == 0 && dy == 0)
            return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx*dx + dy*dy), 0, 1);
        double px = a.X + t * dx, py = a.Y + t * dy;
        return Math.Sqrt((p.X - px)*(p.X - px) + (p.Y - py)*(p.Y - py));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static float[]? ToArray(List<DisposableNamedOnnxValue> outputs, string name)
    {
        var v = outputs.FirstOrDefault(o => o.Name == name);
        if (v is null) return null;
        return TensorToArray(v.AsTensor<float>());
    }

    private static float[]? ToArrayByIndex(List<DisposableNamedOnnxValue> outputs, int index)
    {
        if (index >= outputs.Count) return null;
        return TensorToArray(outputs[index].AsTensor<float>());
    }

    private static float[]? ToArrayByShape(List<DisposableNamedOnnxValue> outputs, int[] shape)
    {
        foreach (var output in outputs)
        {
            var tensor = output.AsTensor<float>();
            if (tensor.Dimensions.SequenceEqual(shape))
                return TensorToArray(tensor);
        }

        return null;
    }

    private static float[] TensorToArray(Tensor<float> t)
    {
        var arr = new float[t.Length];
        int i = 0;
        foreach (var v in t) arr[i++] = v;
        return arr;
    }

    public void ClearEmbedding() { imageEmbed = highResFeats0 = highResFeats1 = null; }

    public void Dispose()
    {
        encoder.Dispose();
        decoder.Dispose();
        textEncoder?.Dispose();
    }
}
