using LabelForge.Core;

namespace LabelForge.Persistence;

/// <summary>
/// Exports annotations in YOLO format (normalized bounding boxes).
/// One .txt file per image: class_id cx_norm cy_norm w_norm h_norm
/// Polygons are exported as YOLO-v8 segmentation format when they have more than 4 vertices.
/// </summary>
public sealed class YoloAnnotationExporter : IAnnotationExporter
{
    public async Task<string> ExportAsync(ImageDocument document, string outputFolder,
        IReadOnlyList<string> orderedLabels, CancellationToken cancellationToken = default)
    {
        if (document.Image is null)
        {
            throw new InvalidOperationException("No image loaded.");
        }

        var imageWidth = (double)document.Image.Width;
        var imageHeight = (double)document.Image.Height;
        var outputPath = Path.Combine(outputFolder,
            Path.GetFileNameWithoutExtension(document.Image.FilePath) + ".txt");

        var lines = new List<string>();
        foreach (var annotation in document.Annotations.Where(a => a.IsVisible))
        {
            var classId = orderedLabels.ToList().IndexOf(annotation.Label);
            if (classId < 0)
            {
                continue;
            }

            var line = BuildLine(classId, annotation, imageWidth, imageHeight);
            if (line is not null)
            {
                lines.Add(line);
            }
        }

        await File.WriteAllLinesAsync(outputPath, lines, cancellationToken);
        return outputPath;
    }

    private static string? BuildLine(int classId, Annotation annotation, double w, double h)
    {
        return annotation.Shape switch
        {
            RectangleShape r => FormatBbox(classId, r.X, r.Y, r.Width, r.Height, w, h),
            EllipseShape e => FormatBbox(classId, e.X, e.Y, e.Width, e.Height, w, h),
            PolygonShape poly when poly.Vertices.Count >= 3 => FormatSegmentation(classId, poly.Points, w, h),
            _ => FormatBboxFromPoints(classId, annotation.Shape.Points, w, h)
        };
    }

    private static string FormatBbox(int classId, double x, double y, double bw, double bh, double iw, double ih)
    {
        var cx = (x + bw / 2) / iw;
        var cy = (y + bh / 2) / ih;
        var nw = bw / iw;
        var nh = bh / ih;
        return F($"{classId} {cx:F6} {cy:F6} {nw:F6} {nh:F6}");
    }

    private static string? FormatBboxFromPoints(int classId, IReadOnlyList<Point2D> points, double iw, double ih)
    {
        if (points.Count == 0) return null;
        var minX = points.Min(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxX = points.Max(p => p.X);
        var maxY = points.Max(p => p.Y);
        return FormatBbox(classId, minX, minY, maxX - minX, maxY - minY, iw, ih);
    }

    private static string FormatSegmentation(int classId, IReadOnlyList<Point2D> points, double iw, double ih)
    {
        var coords = string.Join(" ", points.Select(p => $"{p.X / iw:F6} {p.Y / ih:F6}"));
        return F($"{classId} {coords}");
    }

    private static string F(string s) => s;
}
