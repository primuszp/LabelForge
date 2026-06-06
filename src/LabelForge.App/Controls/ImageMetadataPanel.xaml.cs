using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LabelForge.Core;

namespace LabelForge.App.Controls;

public partial class ImageMetadataPanel : UserControl
{
    private ImageDocument? document;

    public ImageMetadataPanel()
    {
        InitializeComponent();
        BuildPredefinedRows();
    }

    public event EventHandler? AttributeChanged;

    public void SetDocument(ImageDocument? doc)
    {
        document = doc;
        RefreshAll();
    }

    // ── Build predefined attribute rows once at startup ──────────────────────

    private readonly Dictionary<string, List<Button>> optionButtons = new();

    private void BuildPredefinedRows()
    {
        var insertIndex = 0; // insert before the custom-tags separator

        foreach (var def in DefaultAttributeSchema.Definitions)
        {
            var row = BuildAttributeRow(def);
            RootPanel.Children.Insert(insertIndex++, row);
        }
    }

    private FrameworkElement BuildAttributeRow(AttributeDefinition def)
    {
        var panel = new StackPanel { Margin = new Thickness(8, 3, 8, 1) };

        var label = new TextBlock
        {
            Text = def.DisplayName.ToUpperInvariant(),
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 0, 0, 3)
        };
        panel.Children.Add(label);

        var buttonWrap = new WrapPanel { Orientation = Orientation.Horizontal };
        var buttons = new List<Button>();

        foreach (var option in def.Options)
        {
            var baseStyle = TryFindResource("ToggleOptionStyle") as Style
                         ?? TryFindResource("GhostButtonStyle") as Style;
            var btn = new Button
            {
                Content = option.DisplayName,
                Tag = (def.Key, option.Value),
                Style = baseStyle,
                FontSize = 11
            };
            btn.Click += PredefinedOptionOnClick;
            buttons.Add(btn);
            buttonWrap.Children.Add(btn);
        }

        optionButtons[def.Key] = buttons;
        panel.Children.Add(buttonWrap);
        return panel;
    }

    // ── Refresh UI from document ──────────────────────────────────────────────

    private void RefreshAll()
    {
        RefreshPredefined();
        RefreshCustomTags();
    }

    private void RefreshPredefined()
    {
        var activeBg     = (Brush)FindResource("AccentBlueMutedBrush");
        var activeBorder = (Brush)FindResource("AccentBlueBrush");
        var normalBg     = Brushes.Transparent;
        var normalBorder = (Brush)FindResource("BorderBrush");
        var activeText   = (Brush)FindResource("TextOnAccentBrush");
        var normalText   = (Brush)FindResource("TextSecondaryBrush");

        foreach (var (key, buttons) in optionButtons)
        {
            var current = document?.Attributes.GetValueOrDefault(key);
            foreach (var btn in buttons)
            {
                var (_, optValue) = ((string, string))btn.Tag;
                var isActive = current == optValue;
                btn.Background   = isActive ? activeBg     : normalBg;
                btn.BorderBrush  = isActive ? activeBorder : normalBorder;
                btn.Foreground   = isActive ? activeText   : normalText;
            }
        }
    }

    private void RefreshCustomTags()
    {
        CustomTagsPanel.Children.Clear();
        if (document is null) return;

        var predefinedKeys = DefaultAttributeSchema.Definitions.Select(d => d.Key).ToHashSet();

        foreach (var (key, value) in document.Attributes)
        {
            if (predefinedKeys.Contains(key)) continue;
            CustomTagsPanel.Children.Add(BuildCustomTagRow(key, value));
        }
    }

    private FrameworkElement BuildCustomTagRow(string key, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var keyBlock = new TextBlock
        {
            Text = key,
            FontSize = 11,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(keyBlock, 0);

        var valueBlock = new TextBlock
        {
            Text = value,
            FontSize = 11,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(valueBlock, 2);

        var deleteBtn = new Button
        {
            Style = (Style)FindResource("IconButtonStyle"),
            Width = 20,
            Height = 20,
            Tag = key,
            ToolTip = "Tag törlése"
        };
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M3,3 L13,13 M13,3 L3,13"),
            Stroke = (Brush)FindResource("TextMutedBrush"),
            StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Stretch = Stretch.Uniform,
            Width = 8,
            Height = 8,
            Fill = Brushes.Transparent
        };
        deleteBtn.Content = path;
        deleteBtn.Click += DeleteCustomTagOnClick;
        Grid.SetColumn(deleteBtn, 3);

        grid.Children.Add(keyBlock);
        grid.Children.Add(valueBlock);
        grid.Children.Add(deleteBtn);
        return grid;
    }

    // ── Event handlers ───────────────────────────────────────────────────────

    private void PredefinedOptionOnClick(object sender, RoutedEventArgs e)
    {
        if (document is null || sender is not Button btn) return;
        var (key, value) = ((string, string))btn.Tag;

        if (document.Attributes.TryGetValue(key, out var current) && current == value)
            document.Attributes.Remove(key);
        else
            document.Attributes[key] = value;

        document.IsDirty = true;
        RefreshPredefined();
        AttributeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AddCustomTagOnClick(object sender, RoutedEventArgs e)
    {
        CommitCustomTag();
    }

    private void CustomTagBoxOnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CommitCustomTag();
    }

    private void CommitCustomTag()
    {
        if (document is null) return;
        var key = CustomKeyBox.Text.Trim();
        var value = CustomValueBox.Text.Trim();
        if (string.IsNullOrEmpty(key)) return;

        document.Attributes[key] = value;
        document.IsDirty = true;
        CustomKeyBox.Text = string.Empty;
        CustomValueBox.Text = string.Empty;
        RefreshCustomTags();
        AttributeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DeleteCustomTagOnClick(object sender, RoutedEventArgs e)
    {
        if (document is null || sender is not Button { Tag: string key }) return;
        document.Attributes.Remove(key);
        document.IsDirty = true;
        RefreshCustomTags();
        AttributeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearAllOnClick(object sender, RoutedEventArgs e)
    {
        if (document is null) return;
        document.Attributes.Clear();
        document.IsDirty = true;
        RefreshAll();
        AttributeChanged?.Invoke(this, EventArgs.Empty);
    }
}
