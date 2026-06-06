using LabelForge.Core;

namespace LabelForge.App.AI;

public sealed class SegmentationResult
{
    public float X         { get; init; }
    public float Y         { get; init; }
    public float Width     { get; init; }
    public float Height    { get; init; }
    public int   ClassId   { get; init; }
    public float Confidence { get; init; }

    /// <summary>Polygon vertices in original image coordinates.</summary>
    public IReadOnlyList<Point2D> Polygon { get; init; } = [];
}
