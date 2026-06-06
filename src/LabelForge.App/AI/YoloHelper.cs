using Microsoft.ML.OnnxRuntime.Tensors;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LabelForge.App.AI;

internal static class YoloHelper
{
    public const int InputSize = 640;

    // ── Preprocessing ──────────────────────────────────────────────────────

    public static (DenseTensor<float> tensor, float scale, int padX, int padY)
        Preprocess(BitmapSource src)
    {
        float scale = Math.Min((float)InputSize / src.PixelWidth,
                               (float)InputSize / src.PixelHeight);

        var source = src.Format != PixelFormats.Rgb24
            ? (BitmapSource)new FormatConvertedBitmap(src, PixelFormats.Rgb24, null, 0)
            : src;
        var resized = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        var rgb     = new FormatConvertedBitmap(resized, PixelFormats.Rgb24, null, 0);

        int actualW = rgb.PixelWidth;
        int actualH = rgb.PixelHeight;
        int padX    = (InputSize - actualW) / 2;
        int padY    = (InputSize - actualH) / 2;

        int stride = (actualW * 3 + 3) & ~3;
        var pixels = new byte[actualH * stride];
        rgb.CopyPixels(pixels, stride, 0);

        var tensor = new DenseTensor<float>(new[] { 1, 3, InputSize, InputSize });
        const float pad = 114f / 255f;

        for (int c = 0; c < 3; c++)
            for (int y = 0; y < InputSize; y++)
                for (int x = 0; x < InputSize; x++)
                    tensor[0, c, y, x] = pad;

        for (int y = 0; y < actualH; y++)
            for (int x = 0; x < actualW; x++)
            {
                int i = y * stride + x * 3;
                tensor[0, 0, y + padY, x + padX] = pixels[i]     / 255f;
                tensor[0, 1, y + padY, x + padX] = pixels[i + 1] / 255f;
                tensor[0, 2, y + padY, x + padX] = pixels[i + 2] / 255f;
            }

        return (tensor, scale, padX, padY);
    }

    // ── NMS ───────────────────────────────────────────────────────────────

    public static List<T> Nms<T>(List<T> boxes, float iouThresh,
        Func<T, float> x1, Func<T, float> y1, Func<T, float> x2, Func<T, float> y2,
        Func<T, float> conf, Func<T, int> classId)
    {
        var sorted    = boxes.OrderByDescending(conf).ToList();
        var kept      = new List<T>(sorted.Count);
        var suppressed = new bool[sorted.Count];

        for (int i = 0; i < sorted.Count; i++)
        {
            if (suppressed[i]) continue;
            kept.Add(sorted[i]);
            for (int j = i + 1; j < sorted.Count; j++)
            {
                if (!suppressed[j]
                    && classId(sorted[i]) == classId(sorted[j])
                    && Iou(sorted[i], sorted[j], x1, y1, x2, y2) > iouThresh)
                    suppressed[j] = true;
            }
        }
        return kept;
    }

    private static float Iou<T>(T a, T b,
        Func<T, float> x1, Func<T, float> y1, Func<T, float> x2, Func<T, float> y2)
    {
        float ix1 = Math.Max(x1(a), x1(b));
        float iy1 = Math.Max(y1(a), y1(b));
        float ix2 = Math.Min(x2(a), x2(b));
        float iy2 = Math.Min(y2(a), y2(b));
        float inter = Math.Max(0, ix2 - ix1) * Math.Max(0, iy2 - iy1);
        float ua = (x2(a) - x1(a)) * (y2(a) - y1(a));
        float ub = (x2(b) - x1(b)) * (y2(b) - y1(b));
        float union = ua + ub - inter;
        return union <= 0 ? 0 : inter / union;
    }

    // ── Coordinate back-mapping ────────────────────────────────────────────

    public static float ToImgX(float letX, float scale, int padX, int imgW) =>
        Math.Clamp((letX - padX) / scale, 0, imgW);

    public static float ToImgY(float letY, float scale, int padY, int imgH) =>
        Math.Clamp((letY - padY) / scale, 0, imgH);

    // ── Sigmoid ───────────────────────────────────────────────────────────

    public static float Sigmoid(float x) => 1f / (1f + MathF.Exp(-x));
}
