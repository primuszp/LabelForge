using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LabelForge.Core;

namespace LabelForge.App.Controls;

public partial class AnnotationInspectorPanel : UserControl
{
    private ObservableCollection<Annotation>? annotations;
    private Action? invalidateCanvas;
    private Annotation? detailAnnotation;
    private bool suppressDetailEvents;

    public AnnotationInspectorPanel()
    {
        InitializeComponent();
    }

    public event EventHandler<Annotation?>? SelectionChanged;
    public event EventHandler? DeleteSelectedRequested;

    public void SetAnnotations(ObservableCollection<Annotation> collection, Action invalidate)
    {
        annotations = collection;
        invalidateCanvas = invalidate;
        AnnotationList.ItemsSource = collection;
        collection.CollectionChanged += (_, _) => UpdateCount();
        UpdateCount();
    }

    public void SyncSelection(Annotation? selected)
    {
        if (selected is null)
        {
            AnnotationList.SelectedItem = null;
        }
        else
        {
            AnnotationList.SelectedItem = selected;
            AnnotationList.ScrollIntoView(selected);
        }
    }

    public void Refresh()
    {
        AnnotationList.Items.Refresh();
        UpdateCount();
    }

    private void UpdateCount()
    {
        CountLabel.Text = annotations?.Count > 0 ? $"{annotations.Count}" : string.Empty;
    }

    private void AnnotationListOnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (annotations is null) return;

        foreach (var annotation in annotations)
        {
            annotation.IsSelected = AnnotationList.SelectedItems.Contains(annotation);
        }

        invalidateCanvas?.Invoke();
        var single = AnnotationList.SelectedItems.Count == 1
            ? AnnotationList.SelectedItem as Annotation
            : null;
        ShowDetail(single);
        SelectionChanged?.Invoke(this, AnnotationList.SelectedItem as Annotation);
    }

    // ── Detail panel ─────────────────────────────────────────────────────────

    private void ShowDetail(Annotation? annotation)
    {
        detailAnnotation = annotation;
        if (annotation is null)
        {
            DetailPanel.Visibility = Visibility.Collapsed;
            return;
        }

        suppressDetailEvents = true;
        OccludedCheck.IsChecked  = annotation.Occluded;
        TruncatedCheck.IsChecked = annotation.Truncated;
        CrowdCheck.IsChecked     = annotation.Crowd;

        if (annotation.Confidence is double conf)
        {
            ConfidenceSlider.Value = conf;
            ConfidenceLabel.Text   = $"{conf:P0}";
        }
        else
        {
            ConfidenceSlider.Value = 0;
            ConfidenceLabel.Text   = "–";
        }

        suppressDetailEvents = false;
        RefreshAttrList();
        DetailPanel.Visibility = Visibility.Visible;
    }

    private void RefreshAttrList()
    {
        AttrListPanel.Children.Clear();
        if (detailAnnotation is null) return;

        foreach (var (key, value) in detailAnnotation.Attributes)
        {
            AttrListPanel.Children.Add(BuildAttrRow(key, value));
        }
    }

    private FrameworkElement BuildAttrRow(string key, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var keyBlock = new TextBlock
        {
            Text = key, FontSize = 11,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(keyBlock, 0);

        var valueBlock = new TextBlock
        {
            Text = value, FontSize = 11,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(valueBlock, 2);

        var del = new Button
        {
            Style = (Style)FindResource("IconButtonStyle"),
            Width = 18, Height = 18, Tag = key,
            ToolTip = "Törlés"
        };
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M3,3 L13,13 M13,3 L3,13"),
            Stroke = (Brush)FindResource("TextMutedBrush"),
            StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Stretch = Stretch.Uniform, Width = 7, Height = 7,
            Fill = Brushes.Transparent
        };
        del.Content = path;
        del.Click += DeleteAttrOnClick;
        Grid.SetColumn(del, 3);

        grid.Children.Add(keyBlock);
        grid.Children.Add(valueBlock);
        grid.Children.Add(del);
        return grid;
    }

    private void FlagCheckOnClick(object sender, RoutedEventArgs e)
    {
        if (suppressDetailEvents || detailAnnotation is null) return;
        detailAnnotation.Occluded  = OccludedCheck.IsChecked  == true;
        detailAnnotation.Truncated = TruncatedCheck.IsChecked == true;
        detailAnnotation.Crowd     = CrowdCheck.IsChecked     == true;
        MarkDirty();
    }

    private void ConfidenceSliderOnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (suppressDetailEvents || detailAnnotation is null) return;
        var v = e.NewValue;
        detailAnnotation.Confidence = v > 0 ? v : null;
        ConfidenceLabel.Text = v > 0 ? $"{v:P0}" : "–";
        MarkDirty();
    }

    private void AttrBoxOnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CommitAttr();
    }

    private void AddAttrOnClick(object sender, RoutedEventArgs e) => CommitAttr();

    private void CommitAttr()
    {
        if (detailAnnotation is null) return;
        var key   = AttrKeyBox.Text.Trim();
        var value = AttrValueBox.Text.Trim();
        if (string.IsNullOrEmpty(key)) return;
        detailAnnotation.Attributes[key] = value;
        AttrKeyBox.Text   = string.Empty;
        AttrValueBox.Text = string.Empty;
        RefreshAttrList();
        MarkDirty();
    }

    private void DeleteAttrOnClick(object sender, RoutedEventArgs e)
    {
        if (detailAnnotation is null || sender is not Button { Tag: string key }) return;
        detailAnnotation.Attributes.Remove(key);
        RefreshAttrList();
        MarkDirty();
    }

    private void MarkDirty()
    {
        // Bubbles up via the existing SelectionChanged path; invalidate canvas for label rendering
        invalidateCanvas?.Invoke();
    }

    private void ToggleVisibilityOnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Annotation annotation })
        {
            annotation.IsVisible = !annotation.IsVisible;
            invalidateCanvas?.Invoke();
        }
    }

    private void DeleteAnnotationOnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Annotation annotation } && annotations is not null)
        {
            annotations.Remove(annotation);
            invalidateCanvas?.Invoke();
        }
    }

    private void AnnotationColorChangedOnClick(object sender, EventArgs e)
    {
        invalidateCanvas?.Invoke();
    }

    private void DeleteSelectedOnClick(object sender, RoutedEventArgs e)
    {
        DeleteSelectedRequested?.Invoke(this, EventArgs.Empty);
    }
}
