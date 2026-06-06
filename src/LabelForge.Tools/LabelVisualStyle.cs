namespace LabelForge.Tools;

/// <summary>Per-category rendering overrides passed from the host to AnnotationCanvas.</summary>
public sealed class LabelVisualStyle
{
    public double FillOpacity { get; init; } = 0.22;
    public double StrokeThickness { get; init; } = 2.0;
    public bool CategoryVisible { get; init; } = true;
}
