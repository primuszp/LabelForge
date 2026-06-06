namespace LabelForge.App.Models;

public sealed class ProjectFile
{
    public string Version { get; set; } = "1.0";
    public string Name { get; set; } = "Névtelen projekt";
    public string? DatasetFolder { get; set; }
    public List<ProjectLabelClass> LabelClasses { get; set; } = [];
}

public sealed class ProjectLabelClass
{
    public string Name { get; set; } = "object";
    public string ColorHex { get; set; } = "#22c55e";
    public int HotKey { get; set; }
    public double FillOpacity { get; set; } = 0.22;
    public double StrokeThickness { get; set; } = 2.0;
    public bool IsVisible { get; set; } = true;
}
