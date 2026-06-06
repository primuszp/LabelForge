using System.Windows;
using System.Windows.Controls;
using LabelForge.App.ViewModels;
using Microsoft.Win32;

namespace LabelForge.App.Controls;

public partial class DatasetBrowserPanel : UserControl
{
    public DatasetBrowserPanel()
    {
        InitializeComponent();
    }

    public event EventHandler<DatasetImageEntry>? ImageSelected;
    public event EventHandler? FolderOpenRequested;

    public DatasetViewModel? ViewModel { get; set; }

    public void SetViewModel(DatasetViewModel vm)
    {
        ViewModel = vm;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DatasetViewModel.SelectedImage))
            {
                Dispatcher.Invoke(() =>
                {
                    ImageList.SelectedItem = vm.SelectedImage;
                    ImageList.ScrollIntoView(vm.SelectedImage);
                    PositionText.Text = vm.PositionText;
                    PrevButton.IsEnabled = vm.HasPrevious;
                    NextButton.IsEnabled = vm.HasNext;
                });
            }
        };

        vm.Images.CollectionChanged += (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                ImageList.ItemsSource = vm.Images;
                var hasImages = vm.Images.Count > 0;
                ImageList.Visibility = hasImages ? Visibility.Visible : Visibility.Collapsed;
                EmptyStatePanel.Visibility = hasImages ? Visibility.Collapsed : Visibility.Visible;
                PositionText.Text = vm.PositionText;
            });
        };
    }

    public void NavigatePrev() => ViewModel?.NavigatePrev();
    public void NavigateNext() => ViewModel?.NavigateNext();

    private void OpenFolderOnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select dataset folder"
        };

        if (dialog.ShowDialog() == true)
        {
            ViewModel?.LoadFolder(dialog.FolderName);
            FolderOpenRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ImageListOnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ImageList.SelectedItem is DatasetImageEntry entry)
        {
            if (ViewModel?.SelectedImage != entry)
            {
                ViewModel!.SelectedImage = entry;
                ImageSelected?.Invoke(this, entry);
            }
        }
    }

    private void PrevOnClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.NavigatePrev();
    }

    private void NextOnClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.NavigateNext();
    }
}
