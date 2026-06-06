using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LabelForge.App.Controls;

public partial class SimpleColorPicker : UserControl
{
    private static readonly string[] Palette =
    [
        "#ef4444", "#f97316", "#f59e0b", "#eab308", "#84cc16", "#22c55e", "#10b981", "#14b8a6",
        "#06b6d4", "#0ea5e9", "#3b82f6", "#6366f1", "#8b5cf6", "#a855f7", "#d946ef", "#ec4899",
        "#f43f5e", "#7f1d1d", "#7c2d12", "#713f12", "#365314", "#14532d", "#134e4a", "#164e63",
        "#1e3a8a", "#312e81", "#581c87", "#701a75", "#831843", "#111827", "#6b7280", "#ffffff"
    ];

    private bool isUpdating;

    public SimpleColorPicker()
    {
        InitializeComponent();
        BuildPalette();
        UpdateVisuals();
    }

    public event EventHandler? SelectedColorHexChanged;

    public static readonly DependencyProperty SelectedColorHexProperty =
        DependencyProperty.Register(nameof(SelectedColorHex), typeof(string), typeof(SimpleColorPicker),
            new FrameworkPropertyMetadata("#22c55e", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedColorHexChanged));

    public string SelectedColorHex
    {
        get => (string)GetValue(SelectedColorHexProperty);
        set => SetValue(SelectedColorHexProperty, NormalizeHex(value));
    }

    public static readonly DependencyProperty ShowLabelProperty =
        DependencyProperty.Register(nameof(ShowLabel), typeof(bool), typeof(SimpleColorPicker),
            new FrameworkPropertyMetadata(true, (d, _) => ((SimpleColorPicker)d).UpdateVisuals()));

    public bool ShowLabel
    {
        get => (bool)GetValue(ShowLabelProperty);
        set => SetValue(ShowLabelProperty, value);
    }

    private static void OnSelectedColorHexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (SimpleColorPicker)d;
        picker.UpdateVisuals();
        picker.SelectedColorHexChanged?.Invoke(picker, EventArgs.Empty);
    }

    private void BuildPalette()
    {
        foreach (var hex in Palette)
        {
            var button = new Button
            {
                Width = 20,
                Height = 20,
                Margin = new Thickness(2),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(ParseColor(hex)),
                BorderThickness = new Thickness(0),
                Tag = hex,
                ToolTip = hex
            };
            button.Template = CreateSwatchTemplate();
            button.Click += PaletteButtonOnClick;
            PaletteGrid.Children.Add(button);
        }
    }

    private static ControlTemplate CreateSwatchTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
            { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        template.VisualTree = border;
        return template;
    }

    private void PaletteButtonOnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex })
        {
            SelectedColorHex = hex;
            PalettePopup.IsOpen = false;
        }
    }

    private void DropDownButtonOnClick(object sender, RoutedEventArgs e)
    {
        PalettePopup.IsOpen = true;
        HexTextBox.Focus();
        HexTextBox.SelectAll();
    }

    private void HexTextBoxOnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (isUpdating)
        {
            return;
        }

        var normalized = NormalizeHex(HexTextBox.Text);
        if (IsValidColor(normalized))
        {
            SelectedColorHex = normalized;
        }
    }

    private void UpdateVisuals()
    {
        isUpdating = true;
        var normalized = NormalizeHex(SelectedColorHex);
        var color = ParseColor(normalized);
        Swatch.Background = new SolidColorBrush(color);
        ColorText.Text = normalized;
        ColorText.Visibility = ShowLabel ? Visibility.Visible : Visibility.Collapsed;
        HexTextBox.Text = normalized;
        isUpdating = false;
    }

    private static string NormalizeHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "#22c55e";
        }

        var text = value.Trim();
        if (!text.StartsWith('#'))
        {
            text = "#" + text;
        }

        return text;
    }

    private static bool IsValidColor(string value)
    {
        try
        {
            _ = ColorConverter.ConvertFromString(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Color ParseColor(string value)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(value);
        }
        catch
        {
            return Colors.LimeGreen;
        }
    }
}
