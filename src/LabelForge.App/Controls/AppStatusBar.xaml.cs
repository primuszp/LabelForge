using System.Windows.Controls;
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
        ToolText.Text = tool switch
        {
            AnnotationTool.Select => "● Select",
            AnnotationTool.Rectangle => "▭ Rectangle",
            AnnotationTool.Circle => "○ Ellipse",
            AnnotationTool.Polygon => "⬡ Polygon",
            AnnotationTool.Polyline => "⌇ Polyline",
            AnnotationTool.Point => "· Point",
            _ => string.Empty
        };
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
        CountText.Text = count == 0 ? string.Empty : $"{count} annotation{(count == 1 ? "" : "s")}";
    }

    public void ClearCursor() => CursorText.Text = string.Empty;
}
