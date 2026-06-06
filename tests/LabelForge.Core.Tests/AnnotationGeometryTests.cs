using LabelForge.Core;

namespace LabelForge.Core.Tests;

public sealed class AnnotationGeometryTests
{
    [Fact]
    public void RectangleHitTestContainsInteriorPoint()
    {
        var annotation = new Annotation
        {
            Shape = new RectangleShape(new Point2D(10, 20), new Point2D(50, 80))
        };

        Assert.True(AnnotationGeometry.Contains(annotation, new Point2D(30, 40)));
        Assert.False(AnnotationGeometry.Contains(annotation, new Point2D(80, 120), tolerance: 0));
    }

    [Fact]
    public void MoveOffsetsPolygonVertices()
    {
        var polygon = new PolygonShape();
        polygon.Vertices.Add(new Point2D(1, 2));
        polygon.Vertices.Add(new Point2D(3, 4));
        var annotation = new Annotation { Shape = polygon };

        AnnotationGeometry.Move(annotation, 10, -1);

        Assert.Equal(new Point2D(11, 1), polygon.Vertices[0]);
        Assert.Equal(new Point2D(13, 3), polygon.Vertices[1]);
    }
}
