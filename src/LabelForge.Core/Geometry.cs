namespace LabelForge.Core;

public readonly record struct Point2D(double X, double Y);

public readonly record struct Size2D(double Width, double Height);

public sealed record ImageInfo(string FilePath, int Width, int Height);

