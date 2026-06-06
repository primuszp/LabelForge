using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LabelForge.App.Models;

public sealed class LabelClass : INotifyPropertyChanged
{
    private string name = "object";
    private string colorHex = "#22c55e";
    private int hotKey;
    private int annotationCount;
    private double fillOpacity = 0.22;
    private double strokeThickness = 2.0;
    private bool isVisible = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => name;
        set { name = value; OnPropertyChanged(); }
    }

    public string ColorHex
    {
        get => colorHex;
        set { colorHex = value; OnPropertyChanged(); }
    }

    public int HotKey
    {
        get => hotKey;
        set { hotKey = value; OnPropertyChanged(); }
    }

    public int AnnotationCount
    {
        get => annotationCount;
        set { annotationCount = value; OnPropertyChanged(); }
    }

    /// <summary>Fill opacity 0–1 (default 0.22).</summary>
    public double FillOpacity
    {
        get => fillOpacity;
        set { fillOpacity = Math.Clamp(value, 0.0, 1.0); OnPropertyChanged(); }
    }

    /// <summary>Stroke width in image pixels at zoom=1 (default 2).</summary>
    public double StrokeThickness
    {
        get => strokeThickness;
        set { strokeThickness = Math.Clamp(value, 0.5, 16.0); OnPropertyChanged(); }
    }

    /// <summary>Category-level visibility toggle.</summary>
    public bool IsVisible
    {
        get => isVisible;
        set { isVisible = value; OnPropertyChanged(); }
    }

    public override string ToString() => Name;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
