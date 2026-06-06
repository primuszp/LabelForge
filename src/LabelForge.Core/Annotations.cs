using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LabelForge.Core;

public enum AnnotationShapeKind
{
    Rectangle,
    Ellipse,
    Polygon,
    Point,
    Line
}

public abstract class AnnotationShape
{
    public abstract AnnotationShapeKind Kind { get; }

    public abstract IReadOnlyList<Point2D> Points { get; }
}

public sealed class RectangleShape : AnnotationShape
{
    public RectangleShape()
    {
    }

    public RectangleShape(Point2D start, Point2D end)
    {
        X = Math.Min(start.X, end.X);
        Y = Math.Min(start.Y, end.Y);
        Width = Math.Abs(end.X - start.X);
        Height = Math.Abs(end.Y - start.Y);
    }

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    public override AnnotationShapeKind Kind => AnnotationShapeKind.Rectangle;

    public override IReadOnlyList<Point2D> Points =>
    [
        new(X, Y),
        new(X + Width, Y),
        new(X + Width, Y + Height),
        new(X, Y + Height)
    ];
}

public sealed class PolygonShape : AnnotationShape
{
    public ObservableCollection<Point2D> Vertices { get; } = [];

    public override AnnotationShapeKind Kind => AnnotationShapeKind.Polygon;

    public override IReadOnlyList<Point2D> Points => Vertices;
}

public sealed class EllipseShape : AnnotationShape
{
    public EllipseShape()
    {
    }

    public EllipseShape(Point2D start, Point2D end)
    {
        X = Math.Min(start.X, end.X);
        Y = Math.Min(start.Y, end.Y);
        Width = Math.Abs(end.X - start.X);
        Height = Math.Abs(end.Y - start.Y);
    }

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public Point2D? RadiusPoint { get; set; }

    public override AnnotationShapeKind Kind => AnnotationShapeKind.Ellipse;

    public override IReadOnlyList<Point2D> Points =>
    [
        new(X, Y),
        new(X + Width, Y),
        new(X + Width, Y + Height),
        new(X, Y + Height)
    ];

    public IReadOnlyList<Point2D> ToPolygon(int segments = 32)
    {
        var points = new List<Point2D>(segments);
        var cx = X + Width / 2;
        var cy = Y + Height / 2;
        var rx = Width / 2;
        var ry = Height / 2;

        for (var i = 0; i < segments; i++)
        {
            var angle = Math.PI * 2 * i / segments;
            points.Add(new Point2D(cx + Math.Cos(angle) * rx, cy + Math.Sin(angle) * ry));
        }

        return points;
    }
}

public sealed class PointShape : AnnotationShape
{
    public Point2D Point { get; set; }

    public override AnnotationShapeKind Kind => AnnotationShapeKind.Point;

    public override IReadOnlyList<Point2D> Points => [Point];
}

public sealed class LineShape : AnnotationShape
{
    public ObservableCollection<Point2D> Vertices { get; } = [];

    public override AnnotationShapeKind Kind => AnnotationShapeKind.Line;

    public override IReadOnlyList<Point2D> Points => Vertices;
}

public sealed class Annotation : INotifyPropertyChanged
{
    private bool isVisible = true;
    private bool isSelected;
    private string color = "#22c55e";
    private bool occluded;
    private bool truncated;
    private bool crowd;
    private double? confidence;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; init; } = Guid.NewGuid();
    public string Label { get; set; } = "object";

    public string Color
    {
        get => color;
        set { color = value; OnPropertyChanged(); }
    }

    public AnnotationShape Shape { get; set; } = new PolygonShape();

    public bool IsSelected
    {
        get => isSelected;
        set { isSelected = value; OnPropertyChanged(); }
    }

    public bool IsVisible
    {
        get => isVisible;
        set { isVisible = value; OnPropertyChanged(); }
    }

    /// <summary>Az annotált terület részben takart.</summary>
    public bool Occluded
    {
        get => occluded;
        set { occluded = value; OnPropertyChanged(); }
    }

    /// <summary>Az annotált terület kép szélén vágott.</summary>
    public bool Truncated
    {
        get => truncated;
        set { truncated = value; OnPropertyChanged(); }
    }

    /// <summary>Csoport / tömeg (COCO crowd flag).</summary>
    public bool Crowd
    {
        get => crowd;
        set { crowd = value; OnPropertyChanged(); }
    }

    /// <summary>Bizonyossági szint 0–1 (null = nincs megadva).</summary>
    public double? Confidence
    {
        get => confidence;
        set { confidence = value; OnPropertyChanged(); }
    }

    /// <summary>Egyéni kulcs-érték attribútumok per annotáció.</summary>
    public Dictionary<string, string> Attributes { get; } = new();

    /// <summary>AI által javasolt annotáció – még nem fogadta el a felhasználó.</summary>
    public bool IsSuggestion { get; set; }

    public override string ToString() => $"{Label} ({Shape.Kind})";

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ImageDocument
{
    public ImageInfo? Image { get; set; }
    public ObservableCollection<Annotation> Annotations { get; } = [];

    /// <summary>Key-value image-level attributes (e.g. season=winter, quality=good).</summary>
    public Dictionary<string, string> Attributes { get; } = new();

    public string? AnnotationFilePath { get; set; }
    public bool IsDirty { get; set; }
}
