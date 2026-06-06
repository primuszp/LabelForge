using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using LabelForge.Core;

namespace LabelForge.App.Converters;

/// <summary>Converts AnnotationShapeKind to an SVG Path geometry string for shape icons.</summary>
public sealed class ShapeKindToIconConverter : IValueConverter
{
    // Compact SVG path data for 16×16 icons
    private static readonly Dictionary<AnnotationShapeKind, string> IconPaths = new()
    {
        // Keretezett téglalap
        [AnnotationShapeKind.Rectangle] = "M1,4 H15 V12 H1 Z",
        // Ellipszis – sima ovális
        [AnnotationShapeKind.Ellipse]   = "M8,3 A6,4.5 0 1,0 8,3.001 Z",
        // Pentagon
        [AnnotationShapeKind.Polygon]   = "M8,1 L14,5 L12,13 L4,13 L2,5 Z",
        // Töröttvonal (szegmensek)
        [AnnotationShapeKind.Line]      = "M1,13 L5,7 L9,10 L13,4",
        // Kis teli kör
        [AnnotationShapeKind.Point]     = "M8,4 A4,4 0 1,0 8,4.001 Z",
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is AnnotationShapeKind kind && IconPaths.TryGetValue(kind, out var pathData))
        {
            return Geometry.Parse(pathData);
        }

        return Geometry.Parse("M0,0");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Converts bool (IsVisible) to an eye/eye-off icon geometry.</summary>
public sealed class VisibilityToIconConverter : IValueConverter
{
    // Szem – látható: sima mandula + pupilla
    private const string EyeOpen =
        "M1,8 C3,4 13,4 15,8 C13,12 3,12 1,8 Z " +
        "M8,5.5 A2.5,2.5 0 1,0 8,10.5 A2.5,2.5 0 1,0 8,5.5 Z";

    // Szem – rejtett: átlós vonal + körvonal halványan
    private const string EyeSlash =
        "M1,8 C3,4 13,4 15,8 C13,12 3,12 1,8 Z " +
        "M8,5.5 A2.5,2.5 0 1,0 8,10.5 A2.5,2.5 0 1,0 8,5.5 Z " +
        "M2,2 L14,14";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isVisible = value is bool b && b;
        return Geometry.Parse(isVisible ? EyeOpen : EyeSlash);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Converts hex color string to SolidColorBrush.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(color);
            }
            catch { /* fall through */ }
        }

        return new SolidColorBrush(Colors.LimeGreen);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Converts int annotation count to display string; returns empty if zero.</summary>
public sealed class CountBadgeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count && count > 0)
        {
            return count.ToString();
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Converts bool to Visibility (True → Visible, False → Collapsed).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var invert = parameter is string s && s == "invert";
        var bval = value is bool b && b;
        if (invert) bval = !bval;
        return bval ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
