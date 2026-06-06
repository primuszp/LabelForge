namespace LabelForge.Core;

public static class AnnotationGeometry
{
    public static bool Contains(Annotation annotation, Point2D point, double tolerance = 6)
    {
        return annotation.Shape switch
        {
            RectangleShape rectangle => point.X >= rectangle.X - tolerance &&
                                        point.Y >= rectangle.Y - tolerance &&
                                        point.X <= rectangle.X + rectangle.Width + tolerance &&
                                        point.Y <= rectangle.Y + rectangle.Height + tolerance,
            EllipseShape ellipse => IsInEllipse(ellipse, point, tolerance),
            PointShape pointShape => Distance(pointShape.Point, point) <= tolerance,
            PolygonShape polygon => IsPointInPolygon(polygon.Vertices, point) ||
                                    IsNearPolyline(polygon.Vertices, point, tolerance, true),
            LineShape line => IsNearPolyline(line.Vertices, point, tolerance, false),
            _ => false
        };
    }

    public static void Move(Annotation annotation, double deltaX, double deltaY)
    {
        switch (annotation.Shape)
        {
            case RectangleShape rectangle:
                rectangle.X += deltaX;
                rectangle.Y += deltaY;
                break;
            case EllipseShape ellipse:
                ellipse.X += deltaX;
                ellipse.Y += deltaY;
                break;
            case PointShape point:
                point.Point = new Point2D(point.Point.X + deltaX, point.Point.Y + deltaY);
                break;
            case PolygonShape polygon:
                for (var i = 0; i < polygon.Vertices.Count; i++)
                {
                    var p = polygon.Vertices[i];
                    polygon.Vertices[i] = new Point2D(p.X + deltaX, p.Y + deltaY);
                }
                break;
            case LineShape line:
                for (var i = 0; i < line.Vertices.Count; i++)
                {
                    var p = line.Vertices[i];
                    line.Vertices[i] = new Point2D(p.X + deltaX, p.Y + deltaY);
                }
                break;
        }
    }

    public static double Distance(Point2D a, Point2D b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static bool IsNearPolyline(IReadOnlyList<Point2D> points, Point2D point, double tolerance, bool close)
    {
        if (points.Count < 2)
        {
            return false;
        }

        for (var i = 0; i < points.Count - 1; i++)
        {
            if (DistanceToSegment(point, points[i], points[i + 1]) <= tolerance)
            {
                return true;
            }
        }

        return close && DistanceToSegment(point, points[^1], points[0]) <= tolerance;
    }

    private static bool IsInEllipse(EllipseShape ellipse, Point2D point, double tolerance)
    {
        var rx = ellipse.Width / 2 + tolerance;
        var ry = ellipse.Height / 2 + tolerance;
        if (rx <= 0 || ry <= 0)
        {
            return false;
        }

        var cx = ellipse.X + ellipse.Width / 2;
        var cy = ellipse.Y + ellipse.Height / 2;
        var nx = (point.X - cx) / rx;
        var ny = (point.Y - cy) / ry;
        return nx * nx + ny * ny <= 1;
    }

    private static bool IsPointInPolygon(IReadOnlyList<Point2D> polygon, Point2D point)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            if (((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y)) &&
                point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) /
                (polygon[j].Y - polygon[i].Y) + polygon[i].X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static double DistanceToSegment(Point2D p, Point2D a, Point2D b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        if (dx == 0 && dy == 0)
        {
            return Distance(p, a);
        }

        var t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy);
        t = Math.Clamp(t, 0, 1);
        return Distance(p, new Point2D(a.X + t * dx, a.Y + t * dy));
    }
}
