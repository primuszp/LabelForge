using System.Windows;
using LabelForge.Core;
using LabelForge.Persistence;
using Microsoft.Win32;

namespace LabelForge.App.Dialogs;

public partial class ExportDialog : Window
{
    private readonly ImageDocument? currentDocument;
    private readonly IReadOnlyList<ImageDocument>? allDocuments;
    private readonly IReadOnlyList<string> orderedLabels;

    public ExportDialog(ImageDocument current, IReadOnlyList<ImageDocument>? allDocs,
        IReadOnlyList<string> labels, Window owner)
    {
        InitializeComponent();
        Owner = owner;
        currentDocument = current;
        allDocuments = allDocs;
        orderedLabels = labels;

        if (allDocs is null || allDocs.Count <= 1)
        {
            EntireFolderRadio.IsEnabled = false;
        }

        if (current.Image is not null)
        {
            OutputFolderBox.Text = System.IO.Path.GetDirectoryName(current.Image.FilePath) ?? string.Empty;
        }
    }

    private void BrowseOutputOnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select output folder" };
        if (dialog.ShowDialog() == true)
        {
            OutputFolderBox.Text = dialog.FolderName;
        }
    }

    private async void ExportOnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(OutputFolderBox.Text))
        {
            MessageBox.Show(this, "Please select an output folder.", "LabelForge",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ExportButton.IsEnabled = false;
        ExportButton.Content = "Exporting…";

        try
        {
            var outputFolder = OutputFolderBox.Text;
            System.IO.Directory.CreateDirectory(outputFolder);

            var docs = EntireFolderRadio.IsChecked == true && allDocuments is not null
                ? allDocuments
                : new[] { currentDocument! };

            if (YoloRadio.IsChecked == true)
            {
                var exporter = new YoloAnnotationExporter();
                foreach (var doc in docs)
                {
                    await exporter.ExportAsync(doc, outputFolder, orderedLabels);
                }
            }
            else if (CocoRadio.IsChecked == true)
            {
                var exporter = new CocoAnnotationExporter();
                await exporter.ExportBatchAsync([.. docs], outputFolder, orderedLabels);
            }
            else if (PascalRadio.IsChecked == true)
            {
                var exporter = new PascalVocAnnotationExporter();
                foreach (var doc in docs)
                {
                    await exporter.ExportAsync(doc, outputFolder, orderedLabels);
                }
            }

            MessageBox.Show(this, $"Export complete.\n\nFiles written to:\n{outputFolder}", "LabelForge",
                MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Export failed:\n{ex.Message}", "LabelForge",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ExportButton.IsEnabled = true;
            ExportButton.Content = "Export";
        }
    }

    private void CancelOnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
