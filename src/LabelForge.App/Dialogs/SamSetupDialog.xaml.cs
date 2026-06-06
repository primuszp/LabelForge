using System.IO;
using System.Windows;
using LabelForge.App.AI;
using Microsoft.Win32;

namespace LabelForge.App.Dialogs;

public partial class SamSetupDialog : Window
{
    private readonly ModelDownloadService downloader = new();
    private CancellationTokenSource? cts;

    public SamSetupDialog(Window owner)
    {
        InitializeComponent();
        Owner = owner;

        // Load SAM presets only
        SamPresetCombo.ItemsSource = PresetModels.All
            .Where(m => m.Kind is YoloKind.SamPackage or YoloKind.SamEncoder or YoloKind.SamDecoder)
            .ToList();
        SamPresetCombo.SelectedIndex = 0;

        // Restore saved paths
        if (!string.IsNullOrEmpty(AutoLabelSettings.SamEncoderPath))
            EncoderPathBox.Text = AutoLabelSettings.SamEncoderPath;
        if (!string.IsNullOrEmpty(AutoLabelSettings.SamDecoderPath))
            DecoderPathBox.Text = AutoLabelSettings.SamDecoderPath;
        if (!string.IsNullOrEmpty(AutoLabelSettings.SamTextEncoderPath))
            TextEncoderPathBox.Text = AutoLabelSettings.SamTextEncoderPath;
    }

    private void SamPresetComboOnSelectionChanged(object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SamPresetCombo.SelectedItem is not PresetModel m) return;
        bool downloaded = PresetModels.IsDownloaded(m);
        SamPresetDesc.Text = (downloaded ? "✓ Letöltve. " : "") + m.Description;
        SamDownloadBtn.Content = downloaded ? "Újra" : "Letölt";
    }

    private async void SamDownloadOnClick(object sender, RoutedEventArgs e)
    {
        if (SamPresetCombo.SelectedItem is not PresetModel m) return;

        cts = new CancellationTokenSource();
        SamDownloadBtn.IsEnabled = false;
        SamProgress.Visibility   = Visibility.Visible;
        SamProgressText.Visibility = Visibility.Visible;

        var progress = new Progress<(int pct, string status)>(r =>
        {
            if (r.pct >= 0) SamProgress.Value = r.pct;
            SamProgressText.Text = r.status;
        });

        try
        {
            if (m.Kind == YoloKind.SamPackage)
            {
                var paths = await downloader.DownloadAndExtractSamPackageAsync(m, progress, cts.Token);
                EncoderPathBox.Text = paths.encoderPath;
                DecoderPathBox.Text = paths.decoderPath;
                SamPresetDesc.Text = $"✓ Letöltve és kicsomagolva:\n{paths.encoderPath}\n{paths.decoderPath}";
                SamDownloadBtn.Content = "Újra";
                return;
            }

            var path = await downloader.DownloadAsync(m, progress, cts.Token);

            // Auto-fill encoder/decoder path
            if (m.Kind == YoloKind.SamEncoder)
                EncoderPathBox.Text = path;
            else if (m.Kind == YoloKind.SamDecoder)
                DecoderPathBox.Text = path;

            SamPresetDesc.Text = "✓ Letöltve: " + path;
            SamDownloadBtn.Content = "Újra";
        }
        catch (OperationCanceledException)
        {
            SamProgressText.Text = "Megszakítva.";
        }
        catch (Exception ex)
        {
            var hint = ex.Message.Contains("404") || ex.Message.Contains("Not Found")
                ? "\n\nHuggingFace URL nem elérhető. Exportáld manuálisan:\n" +
                  "  pip install sam2\n  python export_sam2.py"
                : string.Empty;
            MessageBox.Show(this, $"Letöltési hiba:\n{ex.Message}{hint}",
                "Letöltési hiba", MessageBoxButton.OK, MessageBoxImage.Warning);
            SamProgressText.Text = "Hiba.";
        }
        finally
        {
            SamDownloadBtn.IsEnabled = true;
            cts = null;
        }
    }

    private void BrowseEncoderOnClick(object sender, RoutedEventArgs e)
    {
        var path = BrowseOnnx("Encoder ONNX kiválasztása",
            string.IsNullOrEmpty(AutoLabelSettings.SamEncoderPath)
                ? PresetModels.ModelsFolder
                : Path.GetDirectoryName(AutoLabelSettings.SamEncoderPath)!);
        if (path is not null) EncoderPathBox.Text = path;
    }

    private void BrowseTextEncoderOnClick(object sender, RoutedEventArgs e)
    {
        var path = BrowseOnnx("Text Encoder ONNX kiválasztása", PresetModels.ModelsFolder);
        if (path is not null) TextEncoderPathBox.Text = path;
    }

    private void BrowseDecoderOnClick(object sender, RoutedEventArgs e)
    {
        var path = BrowseOnnx("Decoder ONNX kiválasztása",
            string.IsNullOrEmpty(AutoLabelSettings.SamDecoderPath)
                ? PresetModels.ModelsFolder
                : Path.GetDirectoryName(AutoLabelSettings.SamDecoderPath)!);
        if (path is not null) DecoderPathBox.Text = path;
    }

    private string? BrowseOnnx(string title, string initialDir)
    {
        var dlg = new OpenFileDialog
        {
            Title = title,
            Filter = "ONNX modell|*.onnx|Minden fájl|*.*",
            InitialDirectory = Directory.Exists(initialDir) ? initialDir : string.Empty
        };
        return dlg.ShowDialog(this) == true ? dlg.FileName : null;
    }

    private async void SaveOnClick(object sender, RoutedEventArgs e)
    {
        var enc = EncoderPathBox.Text.Trim();
        var dec = DecoderPathBox.Text.Trim();

        if (!File.Exists(enc))
        {
            MessageBox.Show(this, "Az encoder fájl nem található.", "SAM2 Beállítás",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!File.Exists(dec))
        {
            MessageBox.Show(this, "A decoder fájl nem található.", "SAM2 Beállítás",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AutoLabelSettings.SamEncoderPath = enc;
        AutoLabelSettings.SamDecoderPath = dec;

        var textEnc = TextEncoderPathBox.Text.Trim();
        if (File.Exists(textEnc))
            AutoLabelSettings.SamTextEncoderPath = textEnc;

        await AutoLabelSettings.SaveAsync();
        DialogResult = true;
        Close();
    }

    private void CancelOnClick(object sender, RoutedEventArgs e)
    {
        cts?.Cancel();
        DialogResult = false;
        Close();
    }
}
