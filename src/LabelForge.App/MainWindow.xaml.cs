using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LabelForge.App.Dialogs;
using LabelForge.App.Services;
using LabelForge.App.ViewModels;
using LabelForge.Core;
using LabelForge.Persistence;
using LabelForge.Tools;
using Microsoft.Win32;

namespace LabelForge.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly LabelMeAnnotationStore annotationStore = new();
    private readonly LabelClassService labelService = new();
    private readonly DatasetViewModel datasetViewModel = new();
    private readonly ProjectService projectService = new();
    private ImageDocument document = new();
    private LabelForge.App.AI.SamSession? samSession;
    private CancellationTokenSource? samEncodeCts;
    private string activeLabelName = "object";
    private string activeLabelColor = "#22c55e";

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        AnnotationCanvas.ActiveToolChanged += OnActiveToolChanged;
        AnnotationCanvas.SelectionChanged += OnCanvasSelectionChanged;
        AnnotationCanvas.MouseMove += OnAnnotationCanvasMouseMove;

        DatasetBrowser.SetViewModel(datasetViewModel);
        DatasetBrowser.ImageSelected += OnDatasetImageSelected;
        datasetViewModel.ImageNavigated += OnDatasetImageNavigated;

        LabelPanel.SetService(labelService);
        LabelPanel.ActiveLabelChanged += OnActiveLabelChanged;
        AnnotationInspector.DeleteSelectedRequested += (_, _) => AnnotationCanvas.DeleteSelected();

        ImageMetadataPanel.AttributeChanged += (_, _) => UpdateTitle();
        projectService.ProjectChanged += (_, _) => OnProjectChanged();

        LabelPanel.VisualSettingsChanged += (_, _) => AnnotationCanvas.InvalidateVisual();

        AnnotationCanvas.LabelStyleProvider = labelName =>
        {
            var cls = labelService.Classes.FirstOrDefault(
                c => string.Equals(c.Name, labelName, StringComparison.OrdinalIgnoreCase));
            if (cls is null) return null;
            return new LabelForge.Tools.LabelVisualStyle
            {
                FillOpacity = cls.FillOpacity,
                StrokeThickness = cls.StrokeThickness,
                CategoryVisible = cls.IsVisible
            };
        };

        Document = document;

        MiniMap.SetViewport(Viewport);
        Viewport.ScrollChanged += (_, _) => MiniMap.Refresh();

        AnnotationCanvas.SamPolygonConfirmed += OnSamPolygonConfirmed;
        AnnotationCanvas.SamProvider = pts => SamDecodeAsync(pts);

        AnnotationCanvas.ActiveToolChanged += (_, tool) =>
        {
            SamTextPanel.Visibility = tool == AnnotationTool.Sam
                ? Visibility.Visible : Visibility.Collapsed;
        };

        StatusBar.SetTool(AnnotationTool.Select);
        StatusBar.SetZoom(1.0);

        _ = InitializeAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ImageDocument Document
    {
        get => document;
        private set
        {
            document = value;
            OnPropertyChanged();
            AnnotationInspector.SetAnnotations(document.Annotations, () => AnnotationCanvas.InvalidateVisual());
            ImageMetadataPanel.SetDocument(document);
            document.Annotations.CollectionChanged += (_, _) =>
            {
                LabelPanel.RefreshCounts(document.Annotations);
                AnnotationInspector.Refresh();
                StatusBar.SetAnnotationCount(document.Annotations.Count);
            };
            StatusBar.SetAnnotationCount(document.Annotations.Count);
            LabelPanel.RefreshCounts(document.Annotations);
        }
    }

    public string ActiveLabelName
    {
        get => activeLabelName;
        private set { activeLabelName = value; OnPropertyChanged(); }
    }

    public string ActiveLabelColor
    {
        get => activeLabelColor;
        private set { activeLabelColor = value; OnPropertyChanged(); }
    }

    // ── ICommand bindings for InputBindings ──
    public ICommand OpenImageCommand => new RelayCommand(_ => OpenImage());
    public ICommand SaveCommand => new RelayCommand(_ => _ = SaveCurrentAsync());
    public ICommand SaveAsCommand => new RelayCommand(_ => _ = SaveAsAsync());
    public ICommand UndoCommand => new RelayCommand(_ => AnnotationCanvas.Undo());
    public ICommand RedoCommand => new RelayCommand(_ => AnnotationCanvas.Redo());
    public ICommand SelectAllCommand => new RelayCommand(_ => AnnotationCanvas.SelectAll());
    public ICommand DeleteSelectedCommand => new RelayCommand(_ => AnnotationCanvas.DeleteSelected());

    private async Task InitializeAsync()
    {
        await LabelForge.App.AI.AutoLabelSettings.LoadAsync();
        SamTextBox.Text = LabelForge.App.AI.AutoLabelSettings.SamLastTextPrompt;
        UpdateQuickAiButtons();

        await labelService.LoadAsync();
        LabelPanel.SetService(labelService);
        if (labelService.ActiveClass is { } active)
        {
            ActiveLabelName = active.Name;
            ActiveLabelColor = active.ColorHex;
        }
    }

    // ── Project menu ──

    private void NewProjectOnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Új projekt létrehozása",
            Filter = "LabelForge projekt|*.lfproj",
            FileName = "projekt.lfproj"
        };
        if (dlg.ShowDialog(this) != true) return;

        var name = Path.GetFileNameWithoutExtension(dlg.FileName);
        projectService.New(name);
        _ = projectService.SaveAsync(dlg.FileName, labelService, datasetViewModel.FolderPath);
        UpdateTitle();
    }

    private async void OpenProjectOnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "LabelForge projekt|*.lfproj|Minden fájl|*.*" };
        if (dlg.ShowDialog(this) != true) return;

        var ok = await projectService.OpenAsync(dlg.FileName);
        if (!ok)
        {
            MessageBox.Show(this, "Nem sikerült megnyitni a projektfájlt.", "LabelForge",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        projectService.ApplyToLabelService(labelService);
        LabelPanel.SetService(labelService);
        if (labelService.ActiveClass is { } active)
            OnActiveLabelChanged(this, active);

        if (!string.IsNullOrWhiteSpace(projectService.Current?.DatasetFolder)
            && Directory.Exists(projectService.Current.DatasetFolder))
        {
            datasetViewModel.LoadFolder(projectService.Current.DatasetFolder);
        }

        UpdateTitle();
    }

    private async void SaveProjectOnClick(object sender, RoutedEventArgs e)
    {
        if (projectService.FilePath is null) { SaveProjectAsOnClick(sender, e); return; }
        await projectService.SaveAsync(projectService.FilePath, labelService, datasetViewModel.FolderPath);
        UpdateTitle();
    }

    private async void SaveProjectAsOnClick(object sender, RoutedEventArgs e)
    {
        if (!projectService.IsOpen) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "LabelForge projekt|*.lfproj",
            FileName = projectService.Current!.Name + ".lfproj"
        };
        if (dlg.ShowDialog(this) != true) return;
        await projectService.SaveAsync(dlg.FileName, labelService, datasetViewModel.FolderPath);
        UpdateTitle();
    }

    private void CloseProjectOnClick(object sender, RoutedEventArgs e)
    {
        projectService.Close();
        UpdateTitle();
    }

    private void OnProjectChanged()
    {
        var open = projectService.IsOpen;
        SaveProjectMenuItem.IsEnabled    = open;
        SaveProjectAsMenuItem.IsEnabled  = open;
        CloseProjectMenuItem.IsEnabled   = open;
    }

    // ── File menu ──

    private void OpenImageOnClick(object sender, RoutedEventArgs e) => OpenImage();

    private void OpenFolderOnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select dataset folder" };
        if (dialog.ShowDialog(this) == true)
            datasetViewModel.LoadFolder(dialog.FolderName);
    }

    private async void OpenJsonOnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "LabelMe JSON|*.json|All files|*.*" };
        if (dialog.ShowDialog(this) != true) return;

        var loaded = await annotationStore.LoadAsync(dialog.FileName);
        var imagePath = ResolveImagePath(dialog.FileName, loaded.Image?.FilePath);
        if (imagePath is null)
        {
            MessageBox.Show(this, "JSON loaded, but referenced image not found.", "LabelForge",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            Document = loaded;
            return;
        }

        LoadImage(imagePath, loaded);
        loaded.AnnotationFilePath = dialog.FileName;
        loaded.IsDirty = false;
        UpdateTitle();
    }

    private async void SaveOnClick(object sender, RoutedEventArgs e) => await SaveCurrentAsync();
    private async void SaveAsOnClick(object sender, RoutedEventArgs e) => await SaveAsAsync();
    private void ExitOnClick(object sender, RoutedEventArgs e) => Close();

    // ── Edit menu ──

    private void UndoOnClick(object sender, RoutedEventArgs e) => AnnotationCanvas.Undo();
    private void RedoOnClick(object sender, RoutedEventArgs e) => AnnotationCanvas.Redo();
    private void SelectAllOnClick(object sender, RoutedEventArgs e) => AnnotationCanvas.SelectAll();
    private void DeleteSelectedOnClick(object sender, RoutedEventArgs e) => AnnotationCanvas.DeleteSelected();
    private void DeleteAllOnClick(object sender, RoutedEventArgs e) => AnnotationCanvas.DeleteAll();

    // ── View menu ──

    private void FitOnClick(object sender, RoutedEventArgs e) => FitImageToWindow();

    private void ResetZoomOnClick(object sender, RoutedEventArgs e)
    {
        Viewport.ResetView();
        StatusBar.SetZoom(1.0);
    }

    private void ToggleDatasetPanelOnClick(object sender, RoutedEventArgs e)
    {
        DatasetPanel.Visibility = DatasetPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ToggleInspectorOnClick(object sender, RoutedEventArgs e)
    {
        InspectorPanel.Visibility = InspectorPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ToggleMiniMapOnClick(object sender, RoutedEventArgs e)
    {
        MiniMap.Visibility = MiniMap.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;
    }

    // ── Brightness ──

    private void BrightnessSliderOnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BrightnessOverlay is null) return;
        var v = e.NewValue;
        if (v == 0)
        {
            BrightnessOverlay.Opacity = 0;
        }
        else if (v > 0)
        {
            BrightnessOverlay.Fill = Brushes.White;
            BrightnessOverlay.Opacity = v / 100.0;
        }
        else
        {
            BrightnessOverlay.Fill = Brushes.Black;
            BrightnessOverlay.Opacity = Math.Abs(v) / 100.0;
        }
    }

    private void ResetBrightnessOnClick(object sender, RoutedEventArgs e)
    {
        BrightnessSlider.Value = 0;
    }

    // ── AI Auto-label ──

    private void AutoLabelOnClick(object sender, RoutedEventArgs e)
    {
        OpenAutoLabelDialog(preselect: null);
    }

    private void QuickDetectOnClick(object sender, RoutedEventArgs e)
    {
        if (LabelForge.App.AI.AutoLabelSettings.DetectionReady)
            _ = RunQuickAiAsync(isDetection: true);
        else
            OpenAutoLabelDialog(preselect: true);
    }

    private void QuickSegmentOnClick(object sender, RoutedEventArgs e)
    {
        if (LabelForge.App.AI.AutoLabelSettings.SegmentationReady)
            _ = RunQuickAiAsync(isDetection: false);
        else
            OpenAutoLabelDialog(preselect: false);
    }

    private void OpenAutoLabelDialog(bool? preselect)
    {
        var imagePaths = datasetViewModel.Images.Select(i => i.FilePath).ToList();
        var dlg = new AutoLabelDialog(Document.Image is not null ? Document : null,
                                      imagePaths, this, preselect);
        dlg.ShowDialog();
        UpdateQuickAiButtons();
        AnnotationCanvas.InvalidateVisual();
        AnnotationInspector.Refresh();
    }

    private void UpdateQuickAiButtons()
    {
        if (QuickDetBtn is null || QuickSegBtn is null) return;

        var hasImage = Document.Image is not null;
        QuickDetBtn.IsEnabled = hasImage && LabelForge.App.AI.AutoLabelSettings.DetectionReady;
        QuickSegBtn.IsEnabled = hasImage && LabelForge.App.AI.AutoLabelSettings.SegmentationReady;
    }

    private async Task RunQuickAiAsync(bool isDetection)
    {
        if (Document.Image is null) return;

        var model   = isDetection
            ? LabelForge.App.AI.AutoLabelSettings.DetectionModelPath
            : LabelForge.App.AI.AutoLabelSettings.SegmentationModelPath;
        var classes = LabelForge.App.AI.AutoLabelSettings.ClassNameList;

        QuickDetBtn.IsEnabled = false;
        QuickSegBtn.IsEnabled = false;
        StatusBar.SetAnnotationCount(-1); // will refresh after

        try
        {
            var bitmap = await Task.Run(() =>
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource   = new Uri(Document.Image.FilePath, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            });

            if (isDetection)
            {
                var results = await Task.Run(() =>
                {
                    using var det = new LabelForge.App.AI.YoloV8Detector(model);
                    return det.Detect(bitmap,
                        LabelForge.App.AI.AutoLabelSettings.Confidence,
                        LabelForge.App.AI.AutoLabelSettings.Nms,
                        LabelForge.App.AI.AutoLabelSettings.MaxDet);
                });
                foreach (var r in results)
                {
                    Document.Annotations.Add(new Annotation
                    {
                        Label      = r.ClassId < classes.Count ? classes[r.ClassId] : $"class_{r.ClassId}",
                        Confidence = r.Confidence,
                        IsSuggestion = true,
                        Shape      = new RectangleShape { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height }
                    });
                }
            }
            else
            {
                var results = await Task.Run(() =>
                {
                    using var seg = new LabelForge.App.AI.YoloV8Segmentor(model);
                    return seg.Detect(bitmap,
                        LabelForge.App.AI.AutoLabelSettings.Confidence,
                        LabelForge.App.AI.AutoLabelSettings.Nms,
                        LabelForge.App.AI.AutoLabelSettings.MaxDet,
                        LabelForge.App.AI.AutoLabelSettings.SegmentationMaskThreshold,
                        LabelForge.App.AI.AutoLabelSettings.SegmentationPolygonEpsilon);
                });
                foreach (var r in results)
                {
                    LabelForge.Core.AnnotationShape shape;
                    if (r.Polygon.Count >= 3)
                    {
                        var poly = new PolygonShape();
                        foreach (var pt in r.Polygon) poly.Vertices.Add(pt);
                        shape = poly;
                    }
                    else
                    {
                        shape = new RectangleShape { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height };
                    }
                    Document.Annotations.Add(new Annotation
                    {
                        Label      = r.ClassId < classes.Count ? classes[r.ClassId] : $"class_{r.ClassId}",
                        Confidence = r.Confidence,
                        IsSuggestion = true,
                        Shape      = shape
                    });
                }
            }

            Document.IsDirty = true;
            AnnotationCanvas.InvalidateVisual();
            AnnotationInspector.Refresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"AI hiba: {ex.Message}", "LabelForge",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            UpdateQuickAiButtons();
        }
    }

    private void AcceptAllSuggestionsOnClick(object sender, RoutedEventArgs e)
    {
        foreach (var ann in Document.Annotations.Where(a => a.IsSuggestion))
            ann.IsSuggestion = false;
        Document.IsDirty = true;
        AnnotationCanvas.InvalidateVisual();
        AnnotationInspector.Refresh();
    }

    private void RejectAllSuggestionsOnClick(object sender, RoutedEventArgs e)
    {
        var suggestions = Document.Annotations.Where(a => a.IsSuggestion).ToList();
        foreach (var s in suggestions)
            Document.Annotations.Remove(s);
        Document.IsDirty = true;
        AnnotationCanvas.InvalidateVisual();
    }

    // ── Tool menu / toolbar ──

    // ── SAM2 ──

    private void ToolSamOnClick(object sender, RoutedEventArgs e)
    {
        if (!LabelForge.App.AI.AutoLabelSettings.SamReady)
        {
            SamSetupOnClick(sender, e);
            return;
        }
        AnnotationCanvas.ActiveTool = AnnotationTool.Sam;
    }

    private void SamToolOnChecked(object sender, RoutedEventArgs e)
    {
        if (AnnotationCanvas is null) return;
        if (!LabelForge.App.AI.AutoLabelSettings.SamReady)
        {
            SamSetupOnClick(sender, e);
            SelectToolBtn.IsChecked = true;
            return;
        }
        AnnotationCanvas.ActiveTool = AnnotationTool.Sam;
        EnsureSamSession();
    }

    private void SamSetupOnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Dialogs.SamSetupDialog(this);
        if (dlg.ShowDialog() == true)
        {
            EnsureSamSession();
        }
    }

    private void EnsureSamSession()
    {
        if (!LabelForge.App.AI.AutoLabelSettings.SamReady) return;

        if (samSession is not null) { samSession.Dispose(); samSession = null; }

        try
        {
            samSession = new LabelForge.App.AI.SamSession(
                LabelForge.App.AI.AutoLabelSettings.SamEncoderPath,
                LabelForge.App.AI.AutoLabelSettings.SamDecoderPath,
                LabelForge.App.AI.AutoLabelSettings.SamTextEncoderPath);
            // Start encoding current image
            if (Document.Image?.FilePath is string path)
                StartSamEncoding(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"SAM2 betöltési hiba:\n{ex.Message}", "SAM2",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            samSession = null;
        }
    }

    private void StartSamEncoding(string imagePath)
    {
        if (samSession is null) return;
        samEncodeCts?.Cancel();
        samEncodeCts = new CancellationTokenSource();
        var ct = samEncodeCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var bmp = LoadFrozenBitmap(imagePath);
                await samSession.EncodeAsync(bmp, ct);
                Dispatcher.Invoke(() => StatusBar.SetTool(AnnotationTool.Sam));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                    MessageBox.Show(this, $"SAM2 encoding hiba:\n{ex.Message}", "SAM2",
                        MessageBoxButton.OK, MessageBoxImage.Warning));
            }
        }, ct);
    }

    private async Task<bool[,]?> SamDecodeAsync(
        IReadOnlyList<(LabelForge.Core.Point2D, bool)> points)
    {
        if (samSession?.HasEmbedding != true) return null;
        var text = SamTextBox.Text.Trim();
        return await samSession.DecodeAsync(points,
            string.IsNullOrEmpty(text) ? null : text);
    }

    private void SamTextBoxOnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        // Trigger decode with current text even without click points
        if (samSession?.HasEmbedding == true)
            _ = AnnotationCanvas.TriggerSamWithText();
    }

    private void SamTextBoxOnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        LabelForge.App.AI.AutoLabelSettings.SamLastTextPrompt = SamTextBox.Text;
    }

    private void SamTextClearOnClick(object sender, RoutedEventArgs e)
    {
        SamTextBox.Text = string.Empty;
        AnnotationCanvas.ClearSamState();
    }

    private void OnSamPolygonConfirmed(object? sender,
        IReadOnlyList<LabelForge.Core.Point2D> polygon)
    {
        var shape = new PolygonShape();
        foreach (var pt in polygon) shape.Vertices.Add(pt);
        Document.Annotations.Add(new Annotation
        {
            Label = ActiveLabelName,
            Color = ActiveLabelColor,
            Shape = shape,
            IsSelected = true
        });
        AnnotationInspector.Refresh();
        AnnotationCanvas.InvalidateVisual();
    }

    private static BitmapImage LoadFrozenBitmap(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path, UriKind.Absolute);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private void ToolSelectOnClick(object sender, RoutedEventArgs e) => AnnotationCanvas.ActiveTool = AnnotationTool.Select;
    private void ToolRectangleOnClick(object sender, RoutedEventArgs e) => AnnotationCanvas.ActiveTool = AnnotationTool.Rectangle;
    private void ToolEllipseOnClick(object sender, RoutedEventArgs e) => AnnotationCanvas.ActiveTool = AnnotationTool.Circle;
    private void ToolPolygonOnClick(object sender, RoutedEventArgs e) => AnnotationCanvas.ActiveTool = AnnotationTool.Polygon;
    private void ToolPolylineOnClick(object sender, RoutedEventArgs e) => AnnotationCanvas.ActiveTool = AnnotationTool.Polyline;
    private void ToolPointOnClick(object sender, RoutedEventArgs e) => AnnotationCanvas.ActiveTool = AnnotationTool.Point;

    private void SelectToolOnChecked(object sender, RoutedEventArgs e) { if (AnnotationCanvas is not null) AnnotationCanvas.ActiveTool = AnnotationTool.Select; }
    private void RectangleToolOnChecked(object sender, RoutedEventArgs e) { if (AnnotationCanvas is not null) AnnotationCanvas.ActiveTool = AnnotationTool.Rectangle; }
    private void EllipseToolOnChecked(object sender, RoutedEventArgs e) { if (AnnotationCanvas is not null) AnnotationCanvas.ActiveTool = AnnotationTool.Circle; }
    private void PolygonToolOnChecked(object sender, RoutedEventArgs e) { if (AnnotationCanvas is not null) AnnotationCanvas.ActiveTool = AnnotationTool.Polygon; }
    private void PolylineToolOnChecked(object sender, RoutedEventArgs e) { if (AnnotationCanvas is not null) AnnotationCanvas.ActiveTool = AnnotationTool.Polyline; }
    private void PointToolOnChecked(object sender, RoutedEventArgs e) { if (AnnotationCanvas is not null) AnnotationCanvas.ActiveTool = AnnotationTool.Point; }

    private void OnActiveToolChanged(object? sender, AnnotationTool tool)
    {
        SelectToolBtn.IsChecked  = tool == AnnotationTool.Select;
        RectToolBtn.IsChecked    = tool == AnnotationTool.Rectangle;
        EllipseToolBtn.IsChecked = tool == AnnotationTool.Circle;
        PolygonToolBtn.IsChecked = tool == AnnotationTool.Polygon;
        PolylineToolBtn.IsChecked = tool == AnnotationTool.Polyline;
        PointToolBtn.IsChecked   = tool == AnnotationTool.Point;
        SamToolBtn.IsChecked     = tool == AnnotationTool.Sam;
        StatusBar.SetTool(tool);
    }

    private void OnCanvasSelectionChanged(object? sender, EventArgs e)
    {
        var selected = Document.Annotations.FirstOrDefault(a => a.IsSelected);
        AnnotationInspector.SyncSelection(selected);
    }

    // ── Export ──

    private void ExportOnClick(object sender, RoutedEventArgs e)
    {
        if (Document.Image is null)
        {
            MessageBox.Show(this, "Open an image before exporting.", "LabelForge",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var labels = labelService.Classes.Select(c => c.Name).ToList();
        var dialog = new ExportDialog(Document, null, labels, this);
        dialog.ShowDialog();
    }

    // ── Slider ──

    private void DraftSpacingSliderOnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AnnotationCanvas is not null)
            AnnotationCanvas.DraftPointSpacing = e.NewValue;
    }

    // ── Dataset navigation ──

    private async void OnDatasetImageSelected(object? sender, DatasetImageEntry entry)
    {
        await AutoSaveIfDirtyAsync();
        await LoadImageFromEntryAsync(entry);
    }

    private async void OnDatasetImageNavigated(object? sender, DatasetImageEntry entry)
    {
        // Fired by NavigatePrev/NavigateNext (not by list click)
        await AutoSaveIfDirtyAsync();
        await LoadImageFromEntryAsync(entry);
    }

    private void OpenImage()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff|All files|*.*"
        };
        if (dialog.ShowDialog(this) == true)
            _ = LoadImageFromEntryAsync(new DatasetImageEntry { FilePath = dialog.FileName });
    }

    private async Task LoadImageFromEntryAsync(DatasetImageEntry entry)
    {
        var newDoc = new ImageDocument();
        LoadImage(entry.FilePath, newDoc);
        await TryLoadSidecarAsync(entry.FilePath);
    }

    // ── Label panel ──

    private void OnActiveLabelChanged(object? sender, Models.LabelClass label)
    {
        ActiveLabelName  = label.Name;
        ActiveLabelColor = label.ColorHex;
    }

    // ── Canvas / viewport mouse (status bar updates) ──

    private void OnAnnotationCanvasMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(AnnotationCanvas);
        StatusBar.SetCursor(pos.X, pos.Y);
    }

    private void ViewportOnMouseMove(object sender, MouseEventArgs e)
    {
        StatusBar.SetZoom(Viewport.Zoom);
    }

    // ── Keyboard shortcuts (Window level) ──

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (Keyboard.Modifiers != ModifierKeys.None) return;

        switch (e.Key)
        {
            case Key.A when datasetViewModel.HasPrevious:
                _ = AutoSaveIfDirtyAsync().ContinueWith(_ =>
                    Dispatcher.Invoke(() => datasetViewModel.NavigatePrev()));
                e.Handled = true;
                break;

            case Key.D when datasetViewModel.HasNext:
                _ = AutoSaveIfDirtyAsync().ContinueWith(_ =>
                    Dispatcher.Invoke(() => datasetViewModel.NavigateNext()));
                e.Handled = true;
                break;

            case Key.V: AnnotationCanvas.ActiveTool = AnnotationTool.Select; e.Handled = true; break;
            case Key.R: AnnotationCanvas.ActiveTool = AnnotationTool.Rectangle; e.Handled = true; break;
            case Key.E: AnnotationCanvas.ActiveTool = AnnotationTool.Circle; e.Handled = true; break;
            case Key.P: AnnotationCanvas.ActiveTool = AnnotationTool.Polygon; e.Handled = true; break;
            case Key.O: AnnotationCanvas.ActiveTool = AnnotationTool.Point; e.Handled = true; break;
            case Key.S:
                if (LabelForge.App.AI.AutoLabelSettings.SamReady)
                {
                    AnnotationCanvas.ActiveTool = AnnotationTool.Sam;
                    e.Handled = true;
                }
                break;

            case Key.H:
            {
                var selected = Document.Annotations.FirstOrDefault(a => a.IsSelected);
                if (selected is not null)
                {
                    selected.IsVisible = !selected.IsVisible;
                    AnnotationInspector.Refresh();
                    AnnotationCanvas.InvalidateVisual();
                }
                e.Handled = true;
                break;
            }

            default:
                if (e.Key >= Key.D1 && e.Key <= Key.D9)
                {
                    var hotKey = e.Key - Key.D0;
                    var label = labelService.FindByHotKey(hotKey);
                    if (label is not null)
                    {
                        LabelPanel.SelectLabel(label);
                        OnActiveLabelChanged(this, label);
                        e.Handled = true;
                    }
                }
                break;
        }
    }

    // ── Window closing ──

    private async void WindowOnClosing(object? sender, CancelEventArgs e)
    {
        if (Document.IsDirty)
        {
            var result = MessageBox.Show(this,
                "There are unsaved changes. Save before closing?", "LabelForge",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (result == MessageBoxResult.Yes)
            {
                e.Cancel = true;
                await SaveCurrentAsync();
                Close();
                return;
            }
        }

        await labelService.SaveAsync();
        samEncodeCts?.Cancel();
        samSession?.Dispose();
    }

    // ── Internal helpers ──

    private void LoadImage(string imagePath, ImageDocument targetDocument)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(imagePath);
        bitmap.EndInit();
        bitmap.Freeze();

        ImageView.Source = bitmap;
        ImageView.Width = bitmap.PixelWidth;
        ImageView.Height = bitmap.PixelHeight;
        ImageHost.Width  = bitmap.PixelWidth;
        ImageHost.Height = bitmap.PixelHeight;
        BrightnessOverlay.Width  = bitmap.PixelWidth;
        BrightnessOverlay.Height = bitmap.PixelHeight;
        AnnotationCanvas.Width  = bitmap.PixelWidth;
        AnnotationCanvas.Height = bitmap.PixelHeight;
        targetDocument.Image = new ImageInfo(imagePath, bitmap.PixelWidth, bitmap.PixelHeight);
        Document = targetDocument;
        MiniMap.SetImage(bitmap);
        AnnotationCanvas.ClearSamState();
        if (samSession is not null) StartSamEncoding(imagePath);
        EmptyState.Visibility = Visibility.Collapsed;
        UpdateQuickAiButtons();
        UpdateTitle();

        Dispatcher.BeginInvoke(FitImageToWindow);
    }

    private async Task TryLoadSidecarAsync(string imagePath)
    {
        var jsonPath = Path.ChangeExtension(imagePath, ".json");
        if (!File.Exists(jsonPath)) return;

        var loaded = await annotationStore.LoadAsync(jsonPath);
        LoadImage(imagePath, loaded);
        loaded.AnnotationFilePath = jsonPath;
        loaded.IsDirty = false;
        UpdateTitle();
    }

    private void FitImageToWindow()
    {
        if (Document.Image is null) return;
        var panelW = (DatasetPanel.Visibility == Visibility.Visible ? 224 : 0)
                   + (InspectorPanel.Visibility == Visibility.Visible ? 284 : 0);
        Viewport.FitTo(
            new Size(Document.Image.Width, Document.Image.Height),
            new Size(ActualWidth - panelW - 30, ActualHeight - 100));
        StatusBar.SetZoom(Viewport.Zoom);
    }

    private async Task SaveCurrentAsync()
    {
        if (Document.AnnotationFilePath is null)
        {
            await SaveAsAsync();
            return;
        }

        await annotationStore.SaveAsync(Document, Document.AnnotationFilePath);
        datasetViewModel.RefreshAnnotationState(Document.Image?.FilePath ?? string.Empty);
        Document.IsDirty = false;
        UpdateTitle();
    }

    private async Task SaveAsAsync()
    {
        if (Document.Image is null)
        {
            MessageBox.Show(this, "Open an image before saving.", "LabelForge",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var defaultPath = Path.ChangeExtension(Document.Image.FilePath, ".json");
        var dialog = new SaveFileDialog
        {
            Filter = "LabelMe JSON|*.json",
            FileName = Path.GetFileName(defaultPath),
            InitialDirectory = Path.GetDirectoryName(defaultPath)
        };

        if (dialog.ShowDialog(this) == true)
        {
            await annotationStore.SaveAsync(Document, dialog.FileName);
            Document.AnnotationFilePath = dialog.FileName;
            datasetViewModel.RefreshAnnotationState(Document.Image.FilePath);
            Document.IsDirty = false;
            UpdateTitle();
        }
    }

    private async Task AutoSaveIfDirtyAsync()
    {
        if (Document.IsDirty && Document.AnnotationFilePath is not null)
            await annotationStore.SaveAsync(Document, Document.AnnotationFilePath);
    }

    private void UpdateTitle()
    {
        var filename = Document.Image is not null
            ? Path.GetFileName(Document.Image.FilePath)
            : string.Empty;
        var dirty = Document.IsDirty ? " *" : string.Empty;

        if (projectService.IsOpen)
        {
            var projName = projectService.Current!.Name;
            Title = string.IsNullOrEmpty(filename)
                ? $"LabelForge – [{projName}]"
                : $"LabelForge – [{projName}] {filename}{dirty}";
        }
        else
        {
            Title = string.IsNullOrEmpty(filename) ? "LabelForge" : $"LabelForge – {filename}{dirty}";
        }
    }

    private static string? ResolveImagePath(string jsonPath, string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return null;
        if (Path.IsPathRooted(imagePath) && File.Exists(imagePath)) return imagePath;
        var local = Path.Combine(Path.GetDirectoryName(jsonPath) ?? string.Empty, imagePath);
        return File.Exists(local) ? local : null;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>Minimal relay command for InputBindings.</summary>
internal sealed class RelayCommand(Action<object?> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute(parameter);
}
