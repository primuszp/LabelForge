using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using LabelForge.App.Models;
using LabelForge.App.Services;

namespace LabelForge.App.Controls;

public partial class LabelClassPanel : UserControl
{
    private LabelClassService? service;

    public LabelClassPanel()
    {
        InitializeComponent();
    }

    public event EventHandler<LabelClass>? ActiveLabelChanged;
    public event EventHandler? VisualSettingsChanged;

    public void SetService(LabelClassService svc)
    {
        service = svc;
        LabelList.ItemsSource = svc.Classes;
        if (svc.ActiveClass is not null)
        {
            LabelList.SelectedItem = svc.ActiveClass;
        }
    }

    public void SelectLabel(LabelClass label)
    {
        LabelList.SelectedItem = label;
    }

    public void RefreshCounts(IEnumerable<Core.Annotation> annotations)
    {
        service?.UpdateAnnotationCounts(annotations);
    }

    private void LabelListOnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LabelList.SelectedItem is LabelClass label && service is not null)
        {
            service.SetActive(label);
            ActiveLabelChanged?.Invoke(this, label);
        }
    }

    private void AddLabelOnClick(object sender, RoutedEventArgs e)
    {
        AddLabelRow.Visibility = Visibility.Visible;
        NewLabelTextBox.Focus();
        NewLabelTextBox.SelectAll();
    }

    private void ConfirmAddLabelOnClick(object sender, RoutedEventArgs e) => CommitNewLabel();

    private void CancelAddLabelOnClick(object sender, RoutedEventArgs e)
    {
        AddLabelRow.Visibility = Visibility.Collapsed;
        NewLabelTextBox.Clear();
    }

    private void NewLabelTextBoxOnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitNewLabel();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelAddLabelOnClick(sender, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void CommitNewLabel()
    {
        if (service is null) return;
        var name = NewLabelTextBox.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var newLabel = service.Add(name);
        LabelList.SelectedItem = newLabel;
        AddLabelRow.Visibility = Visibility.Collapsed;
        NewLabelTextBox.Clear();
    }

    private void LabelColorChangedOnClick(object sender, EventArgs e)
    {
        if (service?.ActiveClass is { } active)
        {
            ActiveLabelChanged?.Invoke(this, active);
        }
    }

    private void ToggleCategoryVisibilityOnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LabelClass label })
        {
            label.IsVisible = !label.IsVisible;
            VisualSettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OpenVisualSettingsOnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LabelClass label } btn) return;
        ShowVisualSettingsPopup(btn, label);
    }

    private void ShowVisualSettingsPopup(Button anchor, LabelClass label)
    {
        var popup = new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.Left,
            StaysOpen = false,
            AllowsTransparency = true
        };

        var panelBg = TryFindResource("PanelBgBrush") as Brush ?? Brushes.DimGray;
        var borderBrush = TryFindResource("BorderBrush") as Brush ?? Brushes.Gray;
        var textBrush = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White;
        var mutedBrush = TryFindResource("TextMutedBrush") as Brush ?? Brushes.LightGray;

        var border = new Border
        {
            Background = panelBg,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 10, 12, 12),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
                { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.5 }
        };

        var stack = new StackPanel { MinWidth = 200 };

        // Header
        stack.Children.Add(new TextBlock
        {
            Text = label.Name.ToUpperInvariant(),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = mutedBrush,
            Margin = new Thickness(0, 0, 0, 10)
        });

        // Fill opacity
        stack.Children.Add(MakeSliderRow(
            "Kitöltés átlátszóság",
            label.FillOpacity * 100,
            0, 100,
            v =>
            {
                label.FillOpacity = v / 100.0;
                VisualSettingsChanged?.Invoke(this, EventArgs.Empty);
            },
            v => $"{v:0}%",
            textBrush, mutedBrush));

        stack.Children.Add(new Border { Height = 8 });

        // Stroke thickness
        stack.Children.Add(MakeSliderRow(
            "Border vastagság",
            label.StrokeThickness,
            0.5, 16,
            v =>
            {
                label.StrokeThickness = v;
                VisualSettingsChanged?.Invoke(this, EventArgs.Empty);
            },
            v => $"{v:0.#}px",
            textBrush, mutedBrush));

        border.Child = stack;
        popup.Child = border;
        popup.IsOpen = true;
    }

    private static FrameworkElement MakeSliderRow(
        string labelText,
        double initialValue,
        double min, double max,
        Action<double> onChange,
        Func<double, string> format,
        Brush textBrush,
        Brush mutedBrush)
    {
        var container = new StackPanel();

        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleBlock = new TextBlock
        {
            Text = labelText,
            FontSize = 11,
            Foreground = textBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(titleBlock, 0);

        var valueBlock = new TextBlock
        {
            Text = format(initialValue),
            FontSize = 11,
            Foreground = mutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 36,
            TextAlignment = TextAlignment.Right
        };
        Grid.SetColumn(valueBlock, 1);

        headerRow.Children.Add(titleBlock);
        headerRow.Children.Add(valueBlock);
        container.Children.Add(headerRow);

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = initialValue,
            TickFrequency = (max - min) / 20,
            IsSnapToTickEnabled = false,
            Margin = new Thickness(0, 4, 0, 0)
        };

        slider.ValueChanged += (_, args) =>
        {
            valueBlock.Text = format(args.NewValue);
            onChange(args.NewValue);
        };

        container.Children.Add(slider);
        return container;
    }

    private void RemoveLabelOnClick(object sender, RoutedEventArgs e)    {
        if (sender is Button { Tag: LabelClass label } && service is not null)
        {
            if (service.Classes.Count <= 1)
            {
                MessageBox.Show("At least one label class is required.", "LabelForge",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            service.Remove(label);
            if (service.ActiveClass is not null)
            {
                LabelList.SelectedItem = service.ActiveClass;
                ActiveLabelChanged?.Invoke(this, service.ActiveClass);
            }
        }
    }
}
