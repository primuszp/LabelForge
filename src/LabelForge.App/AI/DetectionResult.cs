namespace LabelForge.App.AI;

public sealed record DetectionResult(
    float X,
    float Y,
    float Width,
    float Height,
    int ClassId,
    float Confidence);
