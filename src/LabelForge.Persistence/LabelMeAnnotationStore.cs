using System.Text.Json;
using System.Text.Json.Serialization;
using LabelForge.Core;

namespace LabelForge.Persistence;

public sealed class LabelMeAnnotationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task SaveAsync(ImageDocument document, string filePath, CancellationToken cancellationToken = default)
    {
        if (document.Image is null)
        {
            throw new InvalidOperationException("No image is loaded.");
        }

        var dto = ToDto(document);
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, dto, JsonOptions, cancellationToken);
        document.AnnotationFilePath = filePath;
        document.IsDirty = false;
    }

    public async Task<ImageDocument> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        var dto = await JsonSerializer.DeserializeAsync<LabelMeDocumentDto>(stream, JsonOptions, cancellationToken)
                  ?? throw new InvalidDataException("Invalid LabelMe JSON.");

        var document = new ImageDocument
        {
            Image = new ImageInfo(dto.ImagePath ?? string.Empty, dto.ImageWidth, dto.ImageHeight),
            AnnotationFilePath = filePath
        };

        if (dto.Attributes is not null)
        {
            foreach (var (key, value) in dto.Attributes)
                document.Attributes[key] = value;
        }

        foreach (var shape in dto.Shapes ?? [])
        {
            document.Annotations.Add(FromDto(shape));
        }

        return document;
    }

    private static LabelMeDocumentDto ToDto(ImageDocument document)
    {
        var image = document.Image!;
        return new LabelMeDocumentDto
        {
            Version = "LabelForge",
            Flags = new Dictionary<string, object>(),
            ImagePath = Path.GetFileName(image.FilePath),
            ImageWidth = image.Width,
            ImageHeight = image.Height,
            ImageData = null,
            Attributes = document.Attributes.Count > 0
                ? new Dictionary<string, string>(document.Attributes)
                : null,
            Shapes = document.Annotations.Select(ToDto).ToList()
        };
    }

    private static LabelMeShapeDto ToDto(Annotation annotation)
    {
        var shapeType = annotation.Shape.Kind switch
        {
            AnnotationShapeKind.Rectangle => "rectangle",
            AnnotationShapeKind.Ellipse => "polygon",
            AnnotationShapeKind.Polygon => "polygon",
            AnnotationShapeKind.Point => "point",
            AnnotationShapeKind.Line => "line",
            _ => "polygon"
        };

        var points = annotation.Shape switch
        {
            RectangleShape r => new List<List<double>>
            {
                new() { r.X, r.Y },
                new() { r.X + r.Width, r.Y + r.Height }
            },
            EllipseShape e => e.ToPolygon().Select(p => new List<double> { p.X, p.Y }).ToList(),
            _ => annotation.Shape.Points.Select(p => new List<double> { p.X, p.Y }).ToList()
        };

        var flags = new Dictionary<string, object>();
        if (annotation.Occluded) flags["occluded"] = true;
        if (annotation.Truncated) flags["truncated"] = true;
        if (annotation.Crowd) flags["crowd"] = true;

        var attrs = annotation.Attributes.Count > 0
            ? new Dictionary<string, string>(annotation.Attributes)
            : null;

        return new LabelMeShapeDto
        {
            Label = annotation.Label,
            ShapeType = shapeType,
            Points = points,
            LineColor = annotation.Color,
            FillColor = annotation.Color,
            Flags = flags,
            Confidence = annotation.Confidence,
            Attributes = attrs
        };
    }

    private static Annotation FromDto(LabelMeShapeDto dto)
    {
        var points = (dto.Points ?? []).Where(p => p.Count >= 2).Select(p => new Point2D(p[0], p[1])).ToList();
        AnnotationShape shape = dto.ShapeType switch
        {
            "rectangle" when points.Count >= 2 => new RectangleShape(points[0], points[1]),
            "point" when points.Count >= 1 => new PointShape { Point = points[0] },
            "line" => CreateLine(points),
            _ => CreatePolygon(points)
        };

        var annotation = new Annotation
        {
            Label = string.IsNullOrWhiteSpace(dto.Label) ? "object" : dto.Label!,
            Color = string.IsNullOrWhiteSpace(dto.LineColor) ? "#22c55e" : dto.LineColor!,
            Shape = shape,
            Confidence = dto.Confidence
        };

        if (dto.Flags is not null)
        {
            annotation.Occluded  = dto.Flags.ContainsKey("occluded")  && dto.Flags["occluded"] is JsonElement { ValueKind: JsonValueKind.True };
            annotation.Truncated = dto.Flags.ContainsKey("truncated") && dto.Flags["truncated"] is JsonElement { ValueKind: JsonValueKind.True };
            annotation.Crowd     = dto.Flags.ContainsKey("crowd")     && dto.Flags["crowd"]     is JsonElement { ValueKind: JsonValueKind.True };
        }

        if (dto.Attributes is not null)
        {
            foreach (var (k, v) in dto.Attributes)
                annotation.Attributes[k] = v;
        }

        return annotation;
    }

    private static PolygonShape CreatePolygon(IEnumerable<Point2D> points)
    {
        var polygon = new PolygonShape();
        foreach (var point in points)
        {
            polygon.Vertices.Add(point);
        }

        return polygon;
    }

    private static LineShape CreateLine(IEnumerable<Point2D> points)
    {
        var line = new LineShape();
        foreach (var point in points)
        {
            line.Vertices.Add(point);
        }

        return line;
    }

    private sealed class LabelMeDocumentDto
    {
        public string? Version { get; set; }
        public Dictionary<string, object>? Flags { get; set; }
        public List<LabelMeShapeDto>? Shapes { get; set; }
        public string? ImagePath { get; set; }
        public string? ImageData { get; set; }
        public int ImageHeight { get; set; }
        public int ImageWidth { get; set; }
        public Dictionary<string, string>? Attributes { get; set; }
    }

    private sealed class LabelMeShapeDto
    {
        public string? Label { get; set; }
        public List<List<double>>? Points { get; set; }
        public string? GroupId { get; set; }
        public string? Description { get; set; }
        public string ShapeType { get; set; } = "polygon";
        public string? LineColor { get; set; }
        public string? FillColor { get; set; }
        public Dictionary<string, object>? Flags { get; set; }
        public string? Mask { get; set; }
        public double? Confidence { get; set; }
        public Dictionary<string, string>? Attributes { get; set; }
    }
}
