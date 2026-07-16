using System.IO;
using System.Windows;
using System.Windows.Controls;
using LabelForge.Persistence;
using Microsoft.Win32;

namespace LabelForge.App.Dialogs;

public partial class ImportDialog : Window
{
    public ImportDialog(IReadOnlyList<IAnnotationStorePlugin> plugins, Window owner)
    {
        InitializeComponent();
        Owner = owner;
        PluginCombo.ItemsSource = plugins;
        PluginCombo.SelectionChanged += PluginComboOnSelectionChanged;

        if (plugins.Count > 0)
        {
            PluginCombo.SelectedIndex = 0;
        }
    }

    public IAnnotationStorePlugin? SelectedPlugin => PluginCombo.SelectedItem as IAnnotationStorePlugin;
    public string SelectedFolder => FolderBox.Text;

    private void PluginComboOnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        PluginDescriptionText.Text = SelectedPlugin?.Description ?? string.Empty;
    }

    private void BrowseOnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select dataset folder" };
        if (dialog.ShowDialog(this) == true)
        {
            FolderBox.Text = dialog.FolderName;
        }
    }

    private void ImportOnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedPlugin is null)
        {
            MessageBox.Show(this, "Please select a store plugin.", "LabelForge",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(FolderBox.Text) || !Directory.Exists(FolderBox.Text))
        {
            MessageBox.Show(this, "Please select an existing dataset folder.", "LabelForge",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void CancelOnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
