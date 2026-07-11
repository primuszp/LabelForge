using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using LabelForge.App.AI;
using LabelForge.Core;
using LabelForge.Persistence;
using Microsoft.Win32;

namespace LabelForge.App.Dialogs;

public partial class AutoLabelDialog : Window
{
    // ── Állapot helyi mezők (dialóg bezáráskor AutoLabelSettings-be mentve) ─

    private readonly ImageDocument?       document;
    private readonly IReadOnlyList<string> datasetImagePaths;
    private readonly ModelDownloadService  downloader = new();
    private readonly ModelLibraryService   modelLibrary = new();
    private readonly LabelMeAnnotationStore annotationStore = new();
    private CancellationTokenSource?       downloadCts;

    /// <param name="preselect">null=last used, true=detection, false=segmentation</param>
    public AutoLabelDialog(ImageDocument? current, IReadOnlyList<string> datasetPaths, Window owner,
        bool? preselect = null)
    {
        InitializeComponent();
        Owner = owner;
        document = current;
        datasetImagePaths = datasetPaths;

        AllImagesRadio.IsEnabled = datasetPaths.Count > 0;

        // Preset modell lista feltöltése
        PresetCombo.ItemsSource = PresetModels.All;
        PresetCombo.SelectedIndex = 0;

        // Visszatöltés AutoLabelSettings-ből
        bool isDet    = preselect ?? true;
        var modelPath = isDet
            ? AutoLabelSettings.DetectionModelPath
            : AutoLabelSettings.SegmentationModelPath;
        if (!string.IsNullOrEmpty(modelPath))
            ModelPathBox.Text = modelPath;

        ConfSlider.Value        = AutoLabelSettings.Confidence;
        NmsSlider.Value         = AutoLabelSettings.Nms;
        MaxDetSlider.Value      = AutoLabelSettings.MaxDet;
        MaskThresholdSlider.Value = AutoLabelSettings.SegmentationMaskThreshold;
        PolygonEpsilonSlider.Value = AutoLabelSettings.SegmentationPolygonEpsilon;
        ClassNamesBox.Text      = AutoLabelSettings.ClassNames;
        CurrentImageRadio.IsChecked = AutoLabelSettings.CurrentOnly;
        AllImagesRadio.IsChecked    = !AutoLabelSettings.CurrentOnly;
        DetectionRadio.IsChecked    = isDet;
        SegmentationRadio.IsChecked = !isDet;
        RefreshDownloadedModels();
    }

    private void RefreshDownloadedModels()
    {
        var models = modelLibrary.GetDownloadedModels()
            .Where(m => m.Kind is YoloKind.Detection or YoloKind.Segmentation)
            .ToList();
        DownloadedModelCombo.ItemsSource = models;
        DownloadedModelCombo.SelectedItem = models.FirstOrDefault(m =>
            string.Equals(m.FilePath, ModelPathBox.Text, StringComparison.OrdinalIgnoreCase));
    }

    private void DownloadedModelComboOnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (DownloadedModelCombo.SelectedItem is not ModelLibraryEntry model) return;

        ModelPathBox.Text = model.FilePath;
        DetectionRadio.IsChecked = model.Kind == YoloKind.Detection;
        SegmentationRadio.IsChecked = model.Kind == YoloKind.Segmentation;
    }

    // ── Preset ComboBox ──────────────────────────────────────────────────

    private void PresetComboOnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (PresetCombo.SelectedItem is not PresetModel m) return;

        PresetDescText.Text = m.Description;
        bool downloaded = PresetModels.IsDownloaded(m);

        if (downloaded)
        {
            PresetDescText.Text = $"✓ Letöltve: {PresetModels.LocalPath(m)}\n{m.Description}";
            DownloadButton.Content = "Újra";
        }
        else
        {
            DownloadButton.Content = "Letölt";
        }

        // Auto-select model type
        DetectionRadio.IsChecked    = m.Kind == YoloKind.Detection;
        SegmentationRadio.IsChecked = m.Kind == YoloKind.Segmentation;
    }

    private async void DownloadOnClick(object sender, RoutedEventArgs e)
    {
        if (PresetCombo.SelectedItem is not PresetModel m) return;

        downloadCts = new CancellationTokenSource();
        DownloadButton.IsEnabled   = false;
        DownloadProgress.Visibility  = Visibility.Visible;
        DownloadStatusText.Visibility = Visibility.Visible;
        DownloadProgress.Value = 0;

        var progress = new Progress<(int percent, string status)>(rep =>
        {
            if (rep.percent >= 0) DownloadProgress.Value = rep.percent;
            DownloadStatusText.Text = rep.status;
        });

        try
        {
            var path = await downloader.DownloadAsync(m, progress, downloadCts.Token);
            ModelPathBox.Text = path;
            PresetDescText.Text = $"✓ Letöltve: {path}\n{m.Description}";
            DownloadButton.Content = "Újra";
            RefreshDownloadedModels();
        }
        catch (OperationCanceledException)
        {
            DownloadStatusText.Text = "Letöltés megszakítva.";
        }
        catch (Exception ex)
        {
            var hint = ex.Message.Contains("404") || ex.Message.Contains("Not Found")
                ? "\n\nHint: Ez a modell nem elérhető előre-exportált ONNX-ként.\n" +
                  "Exportáld magad: pip install ultralytics && yolo export model=yolov8n.pt format=onnx"
                : string.Empty;
            MessageBox.Show(this, $"Letöltési hiba:\n{ex.Message}{hint}",
                "Letöltési hiba", MessageBoxButton.OK, MessageBoxImage.Warning);
            DownloadStatusText.Text = "Hiba.";
        }
        finally
        {
            DownloadButton.IsEnabled = true;
            downloadCts = null;
        }
    }

    // ── Tallózás ─────────────────────────────────────────────────────────

    private void BrowseModelOnClick(object sender, RoutedEventArgs e)
    {
        bool isDet = DetectionRadio.IsChecked == true;
        var  cur   = isDet ? AutoLabelSettings.DetectionModelPath : AutoLabelSettings.SegmentationModelPath;
        var dlg = new OpenFileDialog
        {
            Filter = "ONNX modell|*.onnx|Minden fájl|*.*",
            InitialDirectory = string.IsNullOrEmpty(cur)
                ? PresetModels.ModelsFolder
                : Path.GetDirectoryName(cur) ?? PresetModels.ModelsFolder
        };
        if (dlg.ShowDialog(this) == true)
        {
            var info = YoloModelInspector.Inspect(dlg.FileName);
            if (!info.IsCompatible)
            {
                MessageBox.Show(this, info.Message, "Nem kompatibilis modell", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ModelPathBox.Text = dlg.FileName;
            DetectionRadio.IsChecked = info.Kind == YoloKind.Detection;
            SegmentationRadio.IsChecked = info.Kind == YoloKind.Segmentation;
            StatusLabel.Text = info.Message;
        }
    }

    // ── Futtatás ──────────────────────────────────────────────────────────

    private async void RunOnClick(object sender, RoutedEventArgs e)
    {
        var modelPath = ModelPathBox.Text.Trim();
        if (!File.Exists(modelPath))
        {
            MessageBox.Show(this, "Válassz érvényes .onnx modell fájlt.", "LabelForge",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        float conf        = (float)ConfSlider.Value;
        float nms         = (float)NmsSlider.Value;
        int   maxDet      = (int)MaxDetSlider.Value;
        float maskThreshold = (float)MaskThresholdSlider.Value;
        double polygonEpsilon = PolygonEpsilonSlider.Value;
        bool  currentOnly = CurrentImageRadio.IsChecked == true;
        bool  isDetection = DetectionRadio.IsChecked == true;

        try
        {
            YoloModelInspector.Require(modelPath, isDetection ? YoloKind.Detection : YoloKind.Segmentation);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Nem kompatibilis modell", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (currentOnly && document?.Image is null)
        {
            MessageBox.Show(this, "Nyiss meg egy képet az aktuális kép feldolgozásához.", "LabelForge",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!currentOnly && datasetImagePaths.Count == 0)
        {
            MessageBox.Show(this, "Nincs betöltött dataset mappa.", "LabelForge",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Értékek megjegyzése AutoLabelSettings-be
        if (isDetection) AutoLabelSettings.DetectionModelPath    = modelPath;
        else             AutoLabelSettings.SegmentationModelPath = modelPath;
        AutoLabelSettings.Confidence  = conf;
        AutoLabelSettings.Nms         = nms;
        AutoLabelSettings.MaxDet      = maxDet;
        AutoLabelSettings.SegmentationMaskThreshold = maskThreshold;
        AutoLabelSettings.SegmentationPolygonEpsilon = polygonEpsilon;
        AutoLabelSettings.ClassNames  = ClassNamesBox.Text;
        AutoLabelSettings.CurrentOnly = currentOnly;

        RunButton.IsEnabled = false;
        CloseButton.IsEnabled = false;
        DownloadButton.IsEnabled = false;
        StatusLabel.Text = "Mentés...";

        try
        {
            await AutoLabelSettings.SaveAsync();
            var classNames = AutoLabelSettings.ClassNameList.ToList();

            int resultCount;
            if (currentOnly)
            {
                resultCount = await RunCurrentImageAsync(modelPath, conf, nms, maxDet,
                    maskThreshold, polygonEpsilon, isDetection, classNames);
            }
            else
            {
                resultCount = await RunDatasetAsync(modelPath, conf, nms, maxDet,
                    maskThreshold, polygonEpsilon, isDetection, classNames);
            }

            if (resultCount == 0)
            {
                StatusLabel.Text = "0 találat";
                MessageBox.Show(this,
                    "A modell sikeresen lefutott, de nem talált támogatott objektumot.\n\n" +
                    "A gyári YOLO11 modellek 80 COCO osztályt ismernek; a pothole például nem része ennek. " +
                    "Próbálj alacsonyabb bizonyossági küszöböt, vagy saját osztályokra tanított modellt.",
                    "Nincs találat", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = string.Empty;
            MessageBox.Show(this, $"AI Auto-Label hiba:\n{BuildErrorMessage(ex, modelPath)}",
                "AI Auto-Label", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RunButton.IsEnabled = true;
            CloseButton.IsEnabled = true;
            DownloadButton.IsEnabled = true;
        }
    }

    private async Task<int> RunCurrentImageAsync(string modelPath, float conf, float nms, int maxDet,
        float maskThreshold, double polygonEpsilon, bool isDetection, List<string> classNames)
    {
        var current = document ?? throw new InvalidOperationException("Nincs aktuális dokumentum.");
        var imagePath = current.Image?.FilePath
            ?? throw new InvalidOperationException("Nincs aktuális kép.");

        StatusLabel.Text = "Feldolgozás...";
        if (isDetection)
        {
            var results = await Task.Run(() =>
            {
                using var detector = new YoloV8Detector(modelPath);
                var bitmap = LoadBitmap(imagePath);
                return detector.Detect(bitmap, conf, nms, maxDet);
            });

            ReplaceSuggestions(current);
            ApplyDetections(current, results, classNames);
            StatusLabel.Text = $"Kész: {results.Count} találat.";
            return results.Count;
        }
        else
        {
            var results = await Task.Run(() =>
            {
                using var segmentor = new YoloV8Segmentor(modelPath);
                var bitmap = LoadBitmap(imagePath);
                return segmentor.Detect(bitmap, conf, nms, maxDet, maskThreshold, polygonEpsilon);
            });

            ReplaceSuggestions(current);
            ApplySegmentations(current, results, classNames);
            StatusLabel.Text = $"Kész: {results.Count} találat.";
            return results.Count;
        }
    }

    private async Task<int> RunDatasetAsync(string modelPath, float conf, float nms, int maxDet,
        float maskThreshold, double polygonEpsilon, bool isDetection, List<string> classNames)
    {
        var totalResults = 0;
        if (isDetection)
        {
            using var detector = new YoloV8Detector(modelPath);
            for (var i = 0; i < datasetImagePaths.Count; i++)
            {
                var idx = i;
                var path = datasetImagePaths[i];
                StatusLabel.Text = $"{idx + 1}/{datasetImagePaths.Count}: {Path.GetFileName(path)}";

                await Task.Run(async () =>
                {
                    var bitmap = LoadBitmap(path);
                    var doc = await LoadOrCreateDocumentAsync(path, bitmap);
                    ReplaceSuggestions(doc);
                    var results = detector.Detect(bitmap, conf, nms, maxDet);
                    ApplyDetections(doc, results, classNames);
                    Interlocked.Add(ref totalResults, results.Count);
                    await SaveSidecarAsync(doc, path);
                });
            }
        }
        else
        {
            using var segmentor = new YoloV8Segmentor(modelPath);
            for (var i = 0; i < datasetImagePaths.Count; i++)
            {
                var idx = i;
                var path = datasetImagePaths[i];
                StatusLabel.Text = $"{idx + 1}/{datasetImagePaths.Count}: {Path.GetFileName(path)}";

                await Task.Run(async () =>
                {
                    var bitmap = LoadBitmap(path);
                    var doc = await LoadOrCreateDocumentAsync(path, bitmap);
                    ReplaceSuggestions(doc);
                    var results = segmentor.Detect(bitmap, conf, nms, maxDet, maskThreshold, polygonEpsilon);
                    ApplySegmentations(doc, results, classNames);
                    Interlocked.Add(ref totalResults, results.Count);
                    await SaveSidecarAsync(doc, path);
                });
            }
        }

        StatusLabel.Text = $"{datasetImagePaths.Count} kép, {totalResults} találat.";
        return totalResults;
    }

    // ── ApplySuggestions ─────────────────────────────────────────────────

    private static void ApplyDetections(ImageDocument doc,
        IEnumerable<DetectionResult> results, IReadOnlyList<string> classNames)
    {
        foreach (var r in results)
        {
            doc.Annotations.Add(new Annotation
            {
                Label        = ClassLabel(r.ClassId, classNames),
                Confidence   = r.Confidence,
                IsSuggestion = true,
                Shape = new RectangleShape { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height }
            });
        }
        doc.IsDirty = true;
    }

    private static void ApplySegmentations(ImageDocument doc,
        IEnumerable<SegmentationResult> results, IReadOnlyList<string> classNames)
    {
        foreach (var r in results)
        {
            AnnotationShape shape;
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

            doc.Annotations.Add(new Annotation
            {
                Label        = ClassLabel(r.ClassId, classNames),
                Confidence   = r.Confidence,
                IsSuggestion = true,
                Shape        = shape
            });
        }
        doc.IsDirty = true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string ClassLabel(int id, IReadOnlyList<string> names) =>
        id < names.Count ? names[id] : $"class_{id}";

    private static void ReplaceSuggestions(ImageDocument doc)
    {
        var suggestions = doc.Annotations.Where(a => a.IsSuggestion).ToList();
        foreach (var suggestion in suggestions)
            doc.Annotations.Remove(suggestion);
    }

    private async Task<ImageDocument> LoadOrCreateDocumentAsync(string imagePath, BitmapSource bitmap)
    {
        var jsonPath = Path.ChangeExtension(imagePath, ".json");
        ImageDocument doc;
        if (File.Exists(jsonPath))
        {
            doc = await annotationStore.LoadAsync(jsonPath);
        }
        else
        {
            doc = new ImageDocument();
        }

        doc.Image = new ImageInfo(imagePath, bitmap.PixelWidth, bitmap.PixelHeight);
        return doc;
    }

    private async Task SaveSidecarAsync(ImageDocument doc, string imagePath)
    {
        var jsonPath = Path.ChangeExtension(imagePath, ".json");
        await annotationStore.SaveAsync(doc, jsonPath);
    }

    private static BitmapSource LoadBitmap(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource   = new Uri(path, UriKind.Absolute);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static string BuildErrorMessage(Exception ex, string modelPath)
    {
        var msg = ex.Message;
        if (msg.Contains(".data") &&
            (msg.Contains("file_size") || msg.Contains("nem tal") ||
             msg.Contains("not found") || msg.Contains("cannot find")))
        {
            return $"A modell külső súlyfájlt igényel:\n{modelPath}.data\n\n" +
                   $"Másold a .onnx.data fájlt a .onnx mellé.\n\nHiba: {msg}";
        }
        return msg;
    }

    private void CloseOnClick(object sender, RoutedEventArgs e)
    {
        downloadCts?.Cancel();
        Close();
    }
}
