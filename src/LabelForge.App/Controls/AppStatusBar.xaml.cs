using System.Windows.Controls;
using System.Windows;
using LabelForge.Tools;

namespace LabelForge.App.Controls;

public partial class AppStatusBar : UserControl
{
    public AppStatusBar()
    {
        InitializeComponent();
    }

    public void SetTool(AnnotationTool tool)
    {
        var (icon, key) = tool switch
        {
            AnnotationTool.Select => ("●", "Tool.Select"),
            AnnotationTool.Rectangle => ("▭", "Tool.Rectangle"),
            AnnotationTool.Circle => ("○", "Tool.Ellipse"),
            AnnotationTool.Polygon => ("⬡", "Tool.Polygon"),
            AnnotationTool.FreehandPolygon => ("✎", "Tool.FreehandPolygon"),
            AnnotationTool.Polyline => ("⌇", "Tool.Polyline"),
            AnnotationTool.Point => ("·", "Tool.Point"),
            _ => (string.Empty, string.Empty)
        };
        var label = Application.Current.TryFindResource(key)?.ToString() ?? tool.ToString();
        ToolText.Text = $"{icon} {label}".Trim();
    }

    public void SetCursor(double x, double y)
    {
        CursorText.Text = $"X: {x:F0}  Y: {y:F0}";
    }

    public void SetZoom(double zoom)
    {
        ZoomText.Text = $"{zoom * 100:F0}%";
    }

    public void SetAnnotationCount(int count)
    {
        var label = Application.Current.TryFindResource("Status.AnnotationCount")?.ToString() ?? "annotations";
        CountText.Text = count == 0 ? string.Empty : $"{count} {label}";
    }

    public void ClearCursor() => CursorText.Text = string.Empty;
}
