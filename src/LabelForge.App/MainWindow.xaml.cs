using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls.Primitives;
using LabelForge.App.Dialogs;
using LabelForge.App.Localization;
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
    private readonly HawkwoodAnnotationStore hawkwoodAnnotationStore = new();
    private CocoDatasetStore? activeCocoDataset;
    private readonly AnnotationStorePluginRegistry storePluginRegistry = AnnotationStorePluginRegistry.CreateDefault();
    private readonly LabelClassService labelService = new();
    private readonly DatasetViewModel datasetViewModel = new();
    private readonly ProjectService projectService = new();
    private readonly LabelMappingService labelMappingService;
    private readonly AiJobService aiJobService = new();
    private LabelForge.App.AI.Sam3OnnxSegmentor? sam3Segmentor;
    private string? sam3ModelDirectory;
    private readonly SemaphoreSlim navigationGate = new(1, 1);
    private ImageDocument document = new();
    private IAnnotationStorePlugin? activeImportPlugin;
    private IAnnotationStorePlugin? currentDocumentPlugin;
    private string activeLabelName = "object";
    private string activeLabelColor = "#22c55e";
    private List<Annotation> annotationClipboard = [];

    public MainWindow()
    {
        labelMappingService = new LabelMappingService(projectService, labelService);
        InitializeComponent();
        DataContext = this;

        AnnotationCanvas.ActiveToolChanged += OnActiveToolChanged;
        AnnotationCanvas.SelectionChanged += OnCanvasSelectionChanged;
        AnnotationCanvas.MouseMove += OnAnnotationCanvasMouseMove;

        DatasetBrowser.SetViewModel(datasetViewModel);
        AiJobsPanel.SetService(aiJobService);
        DatasetBrowser.ImageSelected += OnDatasetImageSelected;
        DatasetBrowser.FolderOpenRequested += path => _ = OpenDatasetFolderAsync(path);
        datasetViewModel.ImageNavigated += OnDatasetImageNavigated;

        LabelPanel.SetService(labelService);
        LabelPanel.ActiveLabelChanged += OnActiveLabelChanged;
        AnnotationInspector.DeleteSelectedRequested += (_, _) => AnnotationCanvas.DeleteSelected();

        ImageMetadataPanel.AttributeChanged += (_, _) => UpdateTitle();
        ReviewPanel.ReviewChanged += (_, _) =>
        {
            ReviewPanel.ApplyTo(Document);
            UpdateTitle();
        };
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

        StatusBar.SetTool(AnnotationTool.Select);
        StatusBar.SetZoom(1.0);
        UpdateLanguageMenu();

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
            ReviewPanel.SetDocument(document);
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
    public ICommand CopyAnnotationsCommand => new RelayCommand(_ => CopySelectedAnnotations());
    public ICommand PasteAnnotationsCommand => new RelayCommand(_ => PasteAnnotations());
    public ICommand DeleteSelectedCommand => new RelayCommand(_ => AnnotationCanvas.DeleteSelected());

    private void CopyAnnotationsOnClick(object sender, RoutedEventArgs e) => CopySelectedAnnotations();
    private void PasteAnnotationsOnClick(object sender, RoutedEventArgs e) => PasteAnnotations();

    private void CopySelectedAnnotations()
    {
        if (Keyboard.FocusedElement is TextBoxBase) return;
        annotationClipboard = Document.Annotations.Where(annotation => annotation.IsSelected)
            .Select(CloneForClipboard).ToList();
    }

    private void PasteAnnotations()
    {
        if (Keyboard.FocusedElement is TextBoxBase || Document.Image is null || annotationClipboard.Count == 0) return;
        AnnotationCanvas.PasteAnnotations(annotationClipboard.Select(CloneForPaste));
        AnnotationInspector.Refresh();
        StatusBar.SetAnnotationCount(Document.Annotations.Count);
        UpdateTitle();
    }

    private static Annotation CloneForClipboard(Annotation source) => CloneAnnotation(source, source.Id);

    private static Annotation CloneForPaste(Annotation source) => CloneAnnotation(source, source.Id, createNewIdentity: true);

    private static Annotation CloneAnnotation(Annotation source, Guid parentId, bool createNewIdentity = false)
    {
        var clone = new Annotation
        {
            Label = source.Label, Color = source.Color, IsVisible = source.IsVisible,
            Occluded = source.Occluded, Truncated = source.Truncated, Crowd = source.Crowd,
            Confidence = source.Confidence, Shape = CloneShape(source.Shape),
            IsSuggestion = false, Source = createNewIdentity ? AnnotationSourceKind.Manual : source.Source,
            WorkflowStatus = createNewIdentity ? AnnotationWorkflowStatus.Pending : source.WorkflowStatus,
            ParentAnnotationId = createNewIdentity ? parentId : source.ParentAnnotationId,
            Revision = 1, ModelName = source.ModelName, ModelVersion = source.ModelVersion
        };
        foreach (var (key, value) in source.Attributes)
            if (!key.StartsWith("coco", StringComparison.OrdinalIgnoreCase)) clone.Attributes[key] = value;
        return clone;
    }

    private static AnnotationShape CloneShape(AnnotationShape shape) => shape switch
    {
        RectangleShape rectangle => new RectangleShape { X = rectangle.X, Y = rectangle.Y, Width = rectangle.Width, Height = rectangle.Height },
        EllipseShape ellipse => new EllipseShape { X = ellipse.X, Y = ellipse.Y, Width = ellipse.Width, Height = ellipse.Height, RadiusPoint = ellipse.RadiusPoint },
        PointShape point => new PointShape { Point = point.Point },
        PolygonShape polygon => CopyVertices(new PolygonShape(), polygon.Vertices),
        LineShape line => CopyVertices(new LineShape(), line.Vertices),
        _ => throw new NotSupportedException($"Nem tamogatott alakzat: {shape.GetType().Name}")
    };

    private static T CopyVertices<T>(T target, IEnumerable<Point2D> vertices) where T : AnnotationShape
    {
        if (target is PolygonShape polygon) foreach (var point in vertices) polygon.Vertices.Add(point);
        if (target is LineShape line) foreach (var point in vertices) line.Vertices.Add(point);
        return target;
    }
    public ICommand PendingStatusCommand => new RelayCommand(_ => SetReviewStatus(AnnotationWorkflowStatus.Pending));
    public ICommand ReviewedStatusCommand => new RelayCommand(_ => SetReviewStatus(AnnotationWorkflowStatus.Reviewed));
    public ICommand ApprovedStatusCommand => new RelayCommand(_ => SetReviewStatus(AnnotationWorkflowStatus.Approved));
    public ICommand RejectedStatusCommand => new RelayCommand(_ => SetReviewStatus(AnnotationWorkflowStatus.Rejected));

    private void SetReviewStatus(AnnotationWorkflowStatus status)
    {
        ReviewPanel.SetStatus(status);
        ReviewPanel.ApplyTo(Document);
        UpdateTitle();
    }

    private void LanguageOnClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem { Tag: string language })
        {
            LocalizationService.Apply(language);
            UpdateLanguageMenu();
        }
    }

    private void UpdateLanguageMenu()
    {
        LanguageAutoMenuItem.IsChecked = LocalizationService.Preference == "auto";
        LanguageEnglishMenuItem.IsChecked = LocalizationService.Preference == "en";
        LanguageHungarianMenuItem.IsChecked = LocalizationService.Preference == "hu";
    }

    private async Task InitializeAsync()
    {
        await LabelForge.App.AI.AutoLabelSettings.LoadAsync();
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

    private async void OpenFolderOnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select dataset folder" };
        if (dialog.ShowDialog(this) == true)
        {
            await OpenDatasetFolderAsync(dialog.FolderName);
        }
    }

    private async Task OpenDatasetFolderAsync(string folderPath)
    {
        var plugin = storePluginRegistry.DetectForFolder(folderPath);
        if (plugin is null)
        {
            activeCocoDataset = null;
            activeImportPlugin = null;
            datasetViewModel.LoadFolder(folderPath);
            return;
        }

        activeCocoDataset = null;
        activeImportPlugin = plugin;
        var progress = new ImportProgressDialog(this);
        progress.Show();
        try
        {
            await datasetViewModel.LoadFolderAsync(folderPath, plugin, progress);
        }
        finally
        {
            progress.Close();
        }
    }

    private async void ImportOnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ImportDialog(storePluginRegistry.Plugins, this);
        if (dialog.ShowDialog() != true || dialog.SelectedPlugin is null)
        {
            return;
        }

        activeCocoDataset = null;
        activeImportPlugin = dialog.SelectedPlugin;
        var progress = new ImportProgressDialog(this);
        progress.Show();
        try
        {
            await datasetViewModel.LoadFolderAsync(dialog.SelectedFolder, activeImportPlugin, progress);
        }
        finally
        {
            progress.Close();
        }
    }

    private async void OpenDatasetOnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "COCO dataset|*.json|Minden fájl|*.*",
            Title = "COCO dataset megnyitása"
        };
        if (dialog.ShowDialog(this) != true) return;

        var progress = new ImportProgressDialog(this);
        progress.Show();
        try
        {
            var store = new CocoDatasetStore();
            await store.OpenAsync(dialog.FileName, progress);
            activeCocoDataset?.Dispose();
            activeCocoDataset = store;
            activeImportPlugin = store;
            labelService.ReplaceWith(store.CategoryNames);
            LabelPanel.RefreshCounts([]);
            if (labelService.ActiveClass is { } activeClass)
            {
                LabelPanel.SelectLabel(activeClass);
                OnActiveLabelChanged(this, activeClass);
            }
            if (store.Index is null) throw new InvalidOperationException("A dataset index nem jott letre.");
            await datasetViewModel.LoadIndexedDatasetAsync(dialog.FileName, store.Index);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "COCO dataset", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            progress.Close();
        }
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
        currentDocumentPlugin = storePluginRegistry.FindById("labelme");
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

    private void LabelMappingOnClick(object sender, RoutedEventArgs e)
    {
        var profiles = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(LabelForge.App.AI.AutoLabelSettings.DetectionModelPath))
            profiles[MappingProfile(LabelForge.App.AI.AutoLabelSettings.DetectionModelPath)] = LabelForge.App.AI.AutoLabelSettings.ClassNameList;
        if (!string.IsNullOrWhiteSpace(LabelForge.App.AI.AutoLabelSettings.SegmentationModelPath))
            profiles[MappingProfile(LabelForge.App.AI.AutoLabelSettings.SegmentationModelPath)] = LabelForge.App.AI.AutoLabelSettings.ClassNameList;
        profiles["sam3"] = LabelForge.App.AI.AutoLabelSettings.Sam3Prompts
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        new LabelMappingDialog(labelMappingService, profiles, this).ShowDialog();
    }

    private static string MappingProfile(string modelPath) => "yolo:" + Path.GetFileName(modelPath).ToLowerInvariant();

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
        var applied = dlg.ShowDialog() == true;
        if (applied)
        {
            foreach (var imagePath in imagePaths)
                datasetViewModel.RefreshAnnotationState(imagePath);
        }
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

        var useSam3 = !isDetection && LabelForge.App.AI.AutoLabelSettings.SegmentationProvider == "SAM3";

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
                var profile = MappingProfile(model);
                var unmapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool AddDetection(LabelForge.App.AI.DetectionResult r)
                {
                    var sourceLabel = r.ClassId < classes.Count ? classes[r.ClassId] : $"class_{r.ClassId}";
                    var projectLabel = labelMappingService.Resolve(profile, sourceLabel);
                    if (projectLabel is null) { unmapped.Add(sourceLabel); return false; }
                    var annotation = new Annotation
                    {
                        Label = projectLabel,
                        Color = ProjectLabelColor(projectLabel),
                        Confidence = r.Confidence,
                        IsSuggestion = true,
                        WorkflowStatus = AnnotationWorkflowStatus.AiGenerated,
                        Source = AnnotationSourceKind.Yolo,
                        ModelName = Path.GetFileName(model),
                        Shape      = new RectangleShape { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height }
                    };
                    annotation.Attributes["ai.source_label"] = sourceLabel;
                    annotation.Attributes["ai.mapping_profile"] = profile;
                    Document.Annotations.Add(annotation);
                    return true;
                }
                var added = results.Count(AddDetection);
                if (added == 0 && unmapped.Count > 0 && EditMappings(profile, unmapped))
                    added = results.Count(AddDetection);
                ShowAiResultIfEmpty(results.Count, added, unmapped.Count);
            }
            else
            {
                var segmentClasses = useSam3
                    ? LabelForge.App.AI.AutoLabelSettings.Sam3Prompts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : classes.ToArray();
                var results = await Task.Run(() =>
                {
                    if (useSam3)
                    {
                        var sam3 = GetSam3Segmentor();
                        return sam3.Detect(bitmap, segmentClasses,
                            LabelForge.App.AI.AutoLabelSettings.Confidence,
                            Math.Min(0.20, LabelForge.App.AI.AutoLabelSettings.SegmentationPolygonEpsilon)).ToList();
                    }
                    using var seg = new LabelForge.App.AI.YoloV8Segmentor(model);
                    return seg.Detect(bitmap,
                        LabelForge.App.AI.AutoLabelSettings.Confidence,
                        LabelForge.App.AI.AutoLabelSettings.Nms,
                        LabelForge.App.AI.AutoLabelSettings.MaxDet,
                        LabelForge.App.AI.AutoLabelSettings.SegmentationMaskThreshold,
                        LabelForge.App.AI.AutoLabelSettings.SegmentationPolygonEpsilon);
                });
                var mappingProfile = useSam3 ? "sam3" : MappingProfile(model);
                var unmapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool AddSegmentation(LabelForge.App.AI.SegmentationResult r)
                {
                    var sourceLabel = r.ClassId < segmentClasses.Length ? segmentClasses[r.ClassId] : $"class_{r.ClassId}";
                    var projectLabel = labelMappingService.Resolve(mappingProfile, sourceLabel);
                    if (projectLabel is null) { unmapped.Add(sourceLabel); return false; }
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
                    var annotation = new Annotation
                    {
                        Label = projectLabel,
                        Color = ProjectLabelColor(projectLabel),
                        Confidence = r.Confidence,
                        IsSuggestion = true,
                        WorkflowStatus = AnnotationWorkflowStatus.AiGenerated,
                        Source = useSam3 ? AnnotationSourceKind.Sam3 : AnnotationSourceKind.Yolo,
                        ModelName = useSam3
                            ? $"SAM3 ONNX ({(sam3Segmentor?.UsesGpu == true ? "CUDA" : "CPU")})"
                            : Path.GetFileName(model),
                        Shape      = shape
                    };
                    annotation.Attributes["ai.source_label"] = sourceLabel;
                    annotation.Attributes["ai.mapping_profile"] = mappingProfile;
                    Document.Annotations.Add(annotation);
                    return true;
                }
                var added = results.Count(AddSegmentation);
                if (added == 0 && unmapped.Count > 0 && EditMappings(mappingProfile, unmapped))
                    added = results.Count(AddSegmentation);
                ShowAiResultIfEmpty(results.Count, added, unmapped.Count);
            }

            Document.WorkflowStatus = AnnotationWorkflowStatus.AiGenerated;
            ReviewPanel.SetDocument(Document);
            Document.IsDirty = true;
            AnnotationCanvas.InvalidateVisual();
            AnnotationInspector.Refresh();
            StatusBar.SetAnnotationCount(Document.Annotations.Count);
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

    private LabelForge.App.AI.Sam3OnnxSegmentor GetSam3Segmentor()
    {
        var directory = LabelForge.App.AI.AutoLabelSettings.Sam3ModelDirectory;
        if (sam3Segmentor is not null && string.Equals(sam3ModelDirectory, directory, StringComparison.OrdinalIgnoreCase))
            return sam3Segmentor;
        sam3Segmentor?.Dispose();
        sam3Segmentor = new LabelForge.App.AI.Sam3OnnxSegmentor(directory);
        sam3ModelDirectory = directory;
        return sam3Segmentor;
    }

    private bool EditMappings(string profile, IEnumerable<string> sourceLabels)
    {
        var sources = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        { [profile] = sourceLabels.OrderBy(label => label).ToArray() };
        return new LabelMappingDialog(labelMappingService, sources, this).ShowDialog() == true;
    }

    private void ShowAiResultIfEmpty(int resultCount, int addedCount, int unmappedCount)
    {
        if (addedCount > 0) return;
        var message = resultCount == 0
            ? "A modell lefutott, de a jelenlegi kuszobokkel nem talalt objektumot."
            : $"A modell {resultCount} objektumot talalt, de {unmappedCount} cimke nincs projektcimkehez rendelve.";
        MessageBox.Show(this, message, "AI eredmeny", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private string ProjectLabelColor(string label) => labelService.Classes
        .FirstOrDefault(item => string.Equals(item.Name, label, StringComparison.OrdinalIgnoreCase))?.ColorHex ?? ActiveLabelColor;

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

    private void ToolSelectOnClick(object sender, RoutedEventArgs e) => AnnotationCanvas.ActiveTool = AnnotationTool.Select;
    private void ToolRectangleOnClick(object sender, RoutedEventArgs e) => AnnotationCanvas.ActiveTool = AnnotationTool.Rectangle;
    private void ToolEllipseOnClick(object sender, RoutedEventArgs e) => AnnotationCanvas.ActiveTool = AnnotationTool.Circle;
    private void ToolPolygonOnClick(object sender, RoutedEventArgs e) => AnnotationCanvas.ActiveTool = AnnotationTool.Polygon;
    private void ToolFreehandOnClick(object sender, RoutedEventArgs e) => AnnotationCanvas.ActiveTool = AnnotationTool.FreehandPolygon;
    private void ToolPolylineOnClick(object sender, RoutedEventArgs e) => AnnotationCanvas.ActiveTool = AnnotationTool.Polyline;
    private void ToolPointOnClick(object sender, RoutedEventArgs e) => AnnotationCanvas.ActiveTool = AnnotationTool.Point;

    private void SelectToolOnChecked(object sender, RoutedEventArgs e) { if (AnnotationCanvas is not null) AnnotationCanvas.ActiveTool = AnnotationTool.Select; }
    private void RectangleToolOnChecked(object sender, RoutedEventArgs e) { if (AnnotationCanvas is not null) AnnotationCanvas.ActiveTool = AnnotationTool.Rectangle; }
    private void EllipseToolOnChecked(object sender, RoutedEventArgs e) { if (AnnotationCanvas is not null) AnnotationCanvas.ActiveTool = AnnotationTool.Circle; }
    private void PolygonToolOnChecked(object sender, RoutedEventArgs e) { if (AnnotationCanvas is not null) AnnotationCanvas.ActiveTool = AnnotationTool.Polygon; }
    private void FreehandToolOnChecked(object sender, RoutedEventArgs e) { if (AnnotationCanvas is not null) AnnotationCanvas.ActiveTool = AnnotationTool.FreehandPolygon; }
    private void PolylineToolOnChecked(object sender, RoutedEventArgs e) { if (AnnotationCanvas is not null) AnnotationCanvas.ActiveTool = AnnotationTool.Polyline; }
    private void PointToolOnChecked(object sender, RoutedEventArgs e) { if (AnnotationCanvas is not null) AnnotationCanvas.ActiveTool = AnnotationTool.Point; }

    private void OnActiveToolChanged(object? sender, AnnotationTool tool)
    {
        SelectToolBtn.IsChecked  = tool == AnnotationTool.Select;
        RectToolBtn.IsChecked    = tool == AnnotationTool.Rectangle;
        EllipseToolBtn.IsChecked = tool == AnnotationTool.Circle;
        PolygonToolBtn.IsChecked = tool == AnnotationTool.Polygon;
        FreehandToolBtn.IsChecked = tool == AnnotationTool.FreehandPolygon;
        PolylineToolBtn.IsChecked = tool == AnnotationTool.Polyline;
        PointToolBtn.IsChecked   = tool == AnnotationTool.Point;
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
        await NavigateToImageAsync(entry);
    }

    private async void OnDatasetImageNavigated(object? sender, DatasetImageEntry entry)
    {
        await NavigateToImageAsync(entry);
    }

    private async Task NavigateToImageAsync(DatasetImageEntry entry)
    {
        await navigationGate.WaitAsync();
        try
        {
            await AutoSaveIfDirtyAsync();
            await LoadImageFromEntryAsync(entry);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"A kép nem nyitható meg:\n{ex.Message}", "LabelForge",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            navigationGate.Release();
        }
    }

    private void OpenImage()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff|All files|*.*"
        };
        if (dialog.ShowDialog(this) == true)
        {
            activeCocoDataset = null;
            activeImportPlugin = null;
            _ = LoadImageFromEntryAsync(new DatasetImageEntry { FilePath = dialog.FileName });
        }
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
                datasetViewModel.NavigatePrev();
                e.Handled = true;
                break;

            case Key.D when datasetViewModel.HasNext:
                datasetViewModel.NavigateNext();
                e.Handled = true;
                break;

            case Key.V: AnnotationCanvas.ActiveTool = AnnotationTool.Select; e.Handled = true; break;
            case Key.R: AnnotationCanvas.ActiveTool = AnnotationTool.Rectangle; e.Handled = true; break;
            case Key.E: AnnotationCanvas.ActiveTool = AnnotationTool.Circle; e.Handled = true; break;
            case Key.P: AnnotationCanvas.ActiveTool = AnnotationTool.Polygon; e.Handled = true; break;
            case Key.B: AnnotationCanvas.ActiveTool = AnnotationTool.FreehandPolygon; e.Handled = true; break;
            case Key.L: AnnotationCanvas.ActiveTool = AnnotationTool.Polyline; e.Handled = true; break;
            case Key.O: AnnotationCanvas.ActiveTool = AnnotationTool.Point; e.Handled = true; break;
            case Key.F: FitImageToWindow(); e.Handled = true; break;
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
        if (Document.IsDirty || activeCocoDataset?.IsDirty == true)
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
        activeCocoDataset?.Dispose();
        await aiJobService.DisposeAsync();
        sam3Segmentor?.Dispose();
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
        currentDocumentPlugin = null;
        MiniMap.SetImage(bitmap);
        EmptyState.Visibility = Visibility.Collapsed;
        UpdateQuickAiButtons();
        UpdateTitle();

        Dispatcher.BeginInvoke(FitImageToWindow);
    }

    private async Task TryLoadSidecarAsync(string imagePath)
    {
        if (activeImportPlugin is not null)
        {
            var imported = await activeImportPlugin.LoadAsync(imagePath);
            if (imported is not null)
            {
                LoadImage(imagePath, imported);
                currentDocumentPlugin = activeImportPlugin;
                imported.IsDirty = false;
                UpdateTitle();
            }

            return;
        }

        var jsonPath = Path.ChangeExtension(imagePath, ".json");
        if (File.Exists(jsonPath))
        {
            var loaded = await annotationStore.LoadAsync(jsonPath);
            LoadImage(imagePath, loaded);
            loaded.AnnotationFilePath = jsonPath;
            loaded.IsDirty = false;
            currentDocumentPlugin = storePluginRegistry.FindById("labelme");
            UpdateTitle();
            return;
        }

        if (hawkwoodAnnotationStore.HasSidecars(imagePath))
        {
            var loaded = await hawkwoodAnnotationStore.LoadAsync(imagePath);
            if (loaded is null)
            {
                return;
            }

            LoadImage(imagePath, loaded);
            loaded.AnnotationFilePath = imagePath;
            loaded.IsDirty = false;
            currentDocumentPlugin = storePluginRegistry.FindById("hawkwood");
            UpdateTitle();
        }
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

        if (currentDocumentPlugin is not null)
        {
            await currentDocumentPlugin.SaveAsync(Document);
        }
        else if (IsHawkwoodDocument(Document))
        {
            await hawkwoodAnnotationStore.SaveAsync(Document);
        }
        else
        {
            await annotationStore.SaveAsync(Document, Document.AnnotationFilePath);
        }

        if (activeCocoDataset is not null)
        {
            await activeCocoDataset.FlushAsync();
        }

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
            currentDocumentPlugin = storePluginRegistry.FindById("labelme");
            datasetViewModel.RefreshAnnotationState(Document.Image.FilePath);
            Document.IsDirty = false;
            UpdateTitle();
        }
    }

    private async Task AutoSaveIfDirtyAsync()
    {
        if (Document.IsDirty && Document.AnnotationFilePath is not null)
        {
            if (currentDocumentPlugin is not null)
            {
                await currentDocumentPlugin.SaveAsync(Document);
            }
            else if (IsHawkwoodDocument(Document))
            {
                await hawkwoodAnnotationStore.SaveAsync(Document);
            }
            else
            {
                await annotationStore.SaveAsync(Document, Document.AnnotationFilePath);
            }

            Document.IsDirty = false;
            datasetViewModel.RefreshAnnotationState(Document.Image?.FilePath ?? string.Empty);
            UpdateTitle();
        }
    }

    private static bool IsHawkwoodDocument(ImageDocument document) =>
        document.Attributes.TryGetValue("source_format", out var sourceFormat)
        && string.Equals(sourceFormat, "HAWKwood", StringComparison.OrdinalIgnoreCase);

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
