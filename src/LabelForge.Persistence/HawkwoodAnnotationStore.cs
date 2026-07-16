using System.Globalization;
using LabelForge.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LabelForge.Persistence;

/// <summary>
/// Reads and writes HAWKwood single-image sidecar annotations.
/// Files are named woodlogs_{image-file}.txt and antiwoodlogs_{image-file}.txt.
/// Each row is x;y;width;height in image pixels.
/// </summary>
public sealed class HawkwoodAnnotationStore : IAnnotationStorePlugin
{
    private readonly Dictionary<string, string> infoCache = new(StringComparer.OrdinalIgnoreCase);
    private const string WoodLogPrefix = "woodlogs_";
    private const string AntiWoodLogPrefix = "antiwoodlogs_";
    private const string WoodLogLabel = "woodlog";
    private const string AntiWoodLogLabel = "antiwoodlog";

    public string Id => "hawkwood";
    public string DisplayName => "HAWKwood";
    public string Description => "HAWKwood woodlogs_/antiwoodlogs_ pontosvesszos bbox sidecar fajlok.";

    public bool CanHandleDataset(string folderPath)
    {
        var normalized = Path.GetFullPath(folderPath).Replace('\\', '/');
        if (normalized.Contains("HAWKwood", StringComparison.OrdinalIgnoreCase)
            || Directory.Exists(Path.Combine(folderPath, "Single Image Benchmark"))
            || Directory.Exists(Path.Combine(folderPath, "Multi Image Benchmark"))) return true;
        return Directory.EnumerateFiles(folderPath, "woodlogs_*", SearchOption.AllDirectories).Take(1).Any();
    }

    public bool IsDatasetImage(string imagePath)
    {
        var name = Path.GetFileNameWithoutExtension(imagePath);
        return !name.EndsWith("_mask2", StringComparison.OrdinalIgnoreCase)
            && !name.StartsWith("SEG_MASK", StringComparison.OrdinalIgnoreCase);
    }

    public DatasetImageGroup? GetDatasetGroup(string datasetRoot, string imagePath)
    {
        var relative = Path.GetRelativePath(datasetRoot, imagePath).Replace('\\', '/');
        var classificationPath = Path.GetFullPath(imagePath).Replace('\\', '/');
        if (classificationPath.Contains("Single Image Benchmark/S.1 and S.2 real/", StringComparison.OrdinalIgnoreCase))
            return GetMaskPath(imagePath) is not null
                ? new("S.1 detektálás + S.2 rönkvég-szegmentálás", "")
                : new("S.1 - Rönkvég-detektálás", "Valós képek - kézi befoglaló téglalap");
        if (classificationPath.Contains("Single Image Benchmark/S.1 synthetic/", StringComparison.OrdinalIgnoreCase))
            return new("S.1 - Rönkvég-detektálás", $"Szintetikus - {GetSegmentAfter(classificationPath, "S.1 synthetic/")}");
        if (classificationPath.Contains("Single Image Benchmark/S.3/", StringComparison.OrdinalIgnoreCase))
        {
            var scene = GetSegmentAfter(classificationPath, "S.3/");
            return Path.GetFileNameWithoutExtension(imagePath).Equals("IMG_L", StringComparison.OrdinalIgnoreCase)
                ? new("S.3 - Teljes homlokfelület szegmentálása", $"{scene} - annotált bal kép")
                : new("S.3 - Teljes homlokfelület szegmentálása", $"{scene} - sztereó referenciakép");
        }
        if (classificationPath.Contains("Multi Image Benchmark/synthetic/", StringComparison.OrdinalIgnoreCase))
        {
            if (Path.GetFileName(imagePath).Equals("ground_truth_binary_cropped.png", StringComparison.OrdinalIgnoreCase))
                return new("M.3 - Szintetikus kontúr-ground-truth", "");
            return new("M.2 + M.3 - Térfogatbecslés", $"Szintetikus / nagy átfedés / {GetSegmentAfter(classificationPath, "synthetic/")}");
        }
        if (classificationPath.Contains("Multi Image Benchmark/real/", StringComparison.OrdinalIgnoreCase))
            return GetRealMultiImageGroup(imagePath, classificationPath);
        return null;
    }

    private DatasetImageGroup GetRealMultiImageGroup(string imagePath, string relative)
    {
        var afterReal = relative[(relative.IndexOf("real/", StringComparison.OrdinalIgnoreCase) + 5)..];
        var parts = afterReal.Split('/');
        var pile = parts.ElementAtOrDefault(0) ?? "ismeretlen jelenet";
        var overlap = parts.ElementAtOrDefault(1)?.Contains("small", StringComparison.OrdinalIgnoreCase) == true
            ? "kis átfedés - panoráma" : "nagy átfedés - 3D rekonstrukció";
        var sequence = parts.ElementAtOrDefault(2) ?? Path.GetFileName(Path.GetDirectoryName(imagePath));
        var pileRoot = Directory.GetParent(Directory.GetParent(Path.GetDirectoryName(imagePath)!)!.FullName)!.FullName;
        var info = infoCache.TryGetValue(pileRoot, out var cached) ? cached : infoCache[pileRoot] = ReadInfo(pileRoot);
        var tasks = new List<string>();
        if (ContainsUsableValue(info, "Number of logs:") || ContainsUsableValue(info, "Numer ob logs:")) tasks.Add("M.1");
        if (ContainsUsableValue(info, "Solid wood volume:")) tasks.Add("M.2");
        if (ContainsUsableValue(info, "Contour volume:")) tasks.Add("M.3");
        var task = tasks.Count == 0 ? "Többképes referencia" : $"{string.Join(" + ", tasks)} - Többképes felmérés";
        return new(task, $"Valós / {overlap} / {pile} / {sequence}");
    }

    private static string ReadInfo(string directory)
    {
        var path = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(p => Path.GetFileName(p).Equals("Info.txt", StringComparison.OrdinalIgnoreCase));
        return path is null ? string.Empty : File.ReadAllText(path);
    }

    private static bool ContainsUsableValue(string info, string key)
    {
        var line = info.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(l => l.StartsWith(key, StringComparison.OrdinalIgnoreCase));
        return line is not null && !line.Contains("n/a", StringComparison.OrdinalIgnoreCase)
            && !line.Contains("not applicable", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSegmentAfter(string path, string marker)
    {
        var start = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return "ismeretlen";
        return path[(start + marker.Length)..].Split('/')[0];
    }

    public bool HasSidecars(string imagePath)
    {
        var directory = Path.GetDirectoryName(imagePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var fileName = Path.GetFileName(imagePath);
        return File.Exists(Path.Combine(directory, $"{WoodLogPrefix}{fileName}.txt"))
            || File.Exists(Path.Combine(directory, $"{AntiWoodLogPrefix}{fileName}.txt"))
            || GetMaskPath(imagePath) is not null;
    }

    bool IAnnotationStorePlugin.HasAnnotations(string imagePath) => HasSidecars(imagePath);

    public async Task<ImageDocument?> LoadAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        var document = new ImageDocument
        {
            Image = new LabelForge.Core.ImageInfo(imagePath, 0, 0),
            AnnotationFilePath = imagePath
        };
        document.Attributes["source_format"] = "HAWKwood";

        await LoadSidecarAsync(document, imagePath, WoodLogPrefix, WoodLogLabel, cancellationToken);
        await LoadSidecarAsync(document, imagePath, AntiWoodLogPrefix, AntiWoodLogLabel, cancellationToken);
        await LoadMaskAsync(document, imagePath, cancellationToken);

        document.IsDirty = false;
        return document;
    }

    public async Task SaveAsync(ImageDocument document, CancellationToken cancellationToken = default)
    {
        if (document.Image is null)
        {
            throw new InvalidOperationException("No image is loaded.");
        }

        await SaveSidecarAsync(document, WoodLogPrefix, WoodLogLabel, cancellationToken);
        await SaveSidecarAsync(document, AntiWoodLogPrefix, AntiWoodLogLabel, cancellationToken);
        await SaveMaskAsync(document, cancellationToken);
        document.AnnotationFilePath = document.Image.FilePath;
        document.IsDirty = false;
    }

    private static async Task LoadSidecarAsync(
        ImageDocument document,
        string imagePath,
        string prefix,
        string label,
        CancellationToken cancellationToken)
    {
        var path = GetSidecarPath(imagePath, prefix);
        if (!File.Exists(path))
        {
            return;
        }

        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        foreach (var line in lines)
        {
            if (TryParseRectangle(line, out var rectangle))
            {
                document.Annotations.Add(new Annotation
                {
                    Label = label,
                    Shape = rectangle
                });
            }
        }
    }

    private static async Task SaveSidecarAsync(
        ImageDocument document,
        string prefix,
        string label,
        CancellationToken cancellationToken)
    {
        var lines = document.Annotations
            .Where(a => string.Equals(a.Label, label, StringComparison.OrdinalIgnoreCase))
            .Select(a => ToRectangle(a.Shape))
            .Where(r => r is not null)
            .Select(r => FormatRectangle(r!))
            .ToArray();

        var path = GetSidecarPath(document.Image!.FilePath, prefix);
        if (lines.Length == 0)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        await File.WriteAllLinesAsync(path, lines, cancellationToken);
    }

    private static string GetSidecarPath(string imagePath, string prefix)
    {
        var directory = Path.GetDirectoryName(imagePath)
            ?? throw new InvalidOperationException("Image path has no directory.");
        return Path.Combine(directory, $"{prefix}{Path.GetFileName(imagePath)}.txt");
    }

    private static bool TryParseRectangle(string line, out RectangleShape rectangle)
    {
        rectangle = new RectangleShape();
        var parts = line.Split(';', StringSplitOptions.TrimEntries);
        if (parts.Length != 4)
        {
            return false;
        }

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
            || !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var height))
        {
            return false;
        }

        rectangle = new RectangleShape
        {
            X = x,
            Y = y,
            Width = width,
            Height = height
        };
        return true;
    }

    private static RectangleShape? ToRectangle(AnnotationShape shape)
    {
        if (shape is RectangleShape rectangle)
        {
            return rectangle;
        }

        var points = shape.Points;
        if (points.Count == 0)
        {
            return null;
        }

        var minX = points.Min(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxX = points.Max(p => p.X);
        var maxY = points.Max(p => p.Y);
        return new RectangleShape
        {
            X = minX,
            Y = minY,
            Width = maxX - minX,
            Height = maxY - minY
        };
    }

    private static string FormatRectangle(RectangleShape rectangle) =>
        string.Join(';',
        [
            FormatNumber(rectangle.X),
            FormatNumber(rectangle.Y),
            FormatNumber(rectangle.Width),
            FormatNumber(rectangle.Height)
        ]);

    private static string FormatNumber(double value) =>
        value.ToString("0.########", CultureInfo.InvariantCulture);

    private static string? GetMaskPath(string imagePath)
    {
        var directory = Path.GetDirectoryName(imagePath);
        if (directory is null) return null;
        var stem = Path.GetFileNameWithoutExtension(imagePath);
        var candidates = stem.Equals("IMG_L", StringComparison.OrdinalIgnoreCase)
            ? new[] { Path.Combine(directory, "SEG_MASK_L.png"), Path.Combine(directory, $"{stem}_mask2.png") }
            : new[] { Path.Combine(directory, $"{stem}_mask2.png") };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task LoadMaskAsync(ImageDocument document, string imagePath, CancellationToken cancellationToken)
    {
        var maskPath = GetMaskPath(imagePath);
        if (maskPath is null) return;
        using var mask = await Image.LoadAsync<L8>(maskPath, cancellationToken);
        foreach (var contour in ExtractContours(mask))
        {
            var simplified = SimplifyClosed(contour, 2.0);
            if (simplified.Count < 3) continue;
            var polygon = new PolygonShape();
            foreach (var point in simplified) polygon.Vertices.Add(point);
            document.Annotations.Add(new Annotation { Label = "woodlog-mask", Shape = polygon });
        }
        document.Attributes["hawkwood_mask_path"] = maskPath;
    }

    private static IReadOnlyList<IReadOnlyList<Point2D>> ExtractContours(Image<L8> mask)
    {
        var edges = new Dictionary<(int X, int Y), Queue<(int X, int Y)>>();
        bool Foreground(int x, int y) => x >= 0 && y >= 0 && x < mask.Width && y < mask.Height && mask[x, y].PackedValue > 127;
        void Add((int X, int Y) start, (int X, int Y) end)
        {
            if (!edges.TryGetValue(start, out var targets)) edges[start] = targets = new Queue<(int, int)>();
            targets.Enqueue(end);
        }
        for (var y = 0; y < mask.Height; y++)
        for (var x = 0; x < mask.Width; x++)
        {
            if (!Foreground(x, y)) continue;
            if (!Foreground(x, y - 1)) Add((x, y), (x + 1, y));
            if (!Foreground(x + 1, y)) Add((x + 1, y), (x + 1, y + 1));
            if (!Foreground(x, y + 1)) Add((x + 1, y + 1), (x, y + 1));
            if (!Foreground(x - 1, y)) Add((x, y + 1), (x, y));
        }
        var contours = new List<IReadOnlyList<Point2D>>();
        while (edges.Values.Any(q => q.Count > 0))
        {
            var start = edges.First(e => e.Value.Count > 0).Key;
            var current = start;
            var contour = new List<Point2D>();
            do
            {
                contour.Add(new Point2D(current.X, current.Y));
                if (!edges.TryGetValue(current, out var next) || next.Count == 0) break;
                current = next.Dequeue();
            } while (current != start && contour.Count <= mask.Width * mask.Height);
            if (current == start && contour.Count >= 12 && SignedArea(contour) > 0) contours.Add(contour);
        }
        return contours;
    }

    private static double SignedArea(IReadOnlyList<Point2D> points) => points.Select((p, i) =>
        p.X * points[(i + 1) % points.Count].Y - points[(i + 1) % points.Count].X * p.Y).Sum() / 2;

    private static IReadOnlyList<Point2D> SimplifyClosed(IReadOnlyList<Point2D> points, double tolerance)
    {
        if (points.Count < 4) return points;
        var result = new List<Point2D>();
        for (var i = 0; i < points.Count; i++)
        {
            var previous = points[(i - 1 + points.Count) % points.Count];
            var current = points[i];
            var next = points[(i + 1) % points.Count];
            var distance = Math.Abs((next.Y - previous.Y) * current.X - (next.X - previous.X) * current.Y
                + next.X * previous.Y - next.Y * previous.X) / Math.Max(1, Math.Sqrt(Math.Pow(next.Y - previous.Y, 2) + Math.Pow(next.X - previous.X, 2)));
            if (distance >= tolerance || i % 8 == 0) result.Add(current);
        }
        return result;
    }

    private static async Task SaveMaskAsync(ImageDocument document, CancellationToken cancellationToken)
    {
        if (!document.Attributes.TryGetValue("hawkwood_mask_path", out var path) || !File.Exists(path)) return;
        using var original = await Image.LoadAsync<L8>(path, cancellationToken);
        using var output = new Image<L8>(original.Width, original.Height, new L8(0));
        foreach (var annotation in document.Annotations.Where(a => a.Label == "woodlog-mask" && a.Shape.Points.Count >= 3))
            FillPolygon(output, annotation.Shape.Points);
        await output.SaveAsPngAsync(path, cancellationToken);
    }

    private static void FillPolygon(Image<L8> image, IReadOnlyList<Point2D> polygon)
    {
        var minY = Math.Max(0, (int)Math.Floor(polygon.Min(p => p.Y)));
        var maxY = Math.Min(image.Height - 1, (int)Math.Ceiling(polygon.Max(p => p.Y)));
        for (var y = minY; y <= maxY; y++)
        {
            var intersections = new List<double>();
            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count];
                if ((a.Y <= y && b.Y > y) || (b.Y <= y && a.Y > y)) intersections.Add(a.X + (y - a.Y) * (b.X - a.X) / (b.Y - a.Y));
            }
            intersections.Sort();
            for (var i = 0; i + 1 < intersections.Count; i += 2)
                for (var x = Math.Max(0, (int)Math.Ceiling(intersections[i])); x <= Math.Min(image.Width - 1, (int)Math.Floor(intersections[i + 1])); x++) image[x, y] = new L8(255);
        }
    }
}
