using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LabelForge.App.ViewModels;
using LabelForge.Core;
using LabelForge.Persistence;
using Microsoft.Win32;

namespace LabelForge.App.Controls;

public partial class DatasetBrowserPanel : UserControl
{
    private CancellationTokenSource? filterDelay;
    public DatasetBrowserPanel()
    {
        InitializeComponent();
    }

    public event EventHandler<DatasetImageEntry>? ImageSelected;
    public event Action<string>? FolderOpenRequested;

    public DatasetViewModel? ViewModel { get; set; }

    public void SetViewModel(DatasetViewModel vm)
    {
        ViewModel = vm;
        MonthFilter.ItemsSource = vm.Months;
        var groupedImages = new ListCollectionView(vm.Images);
        groupedImages.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DatasetImageEntry.TaskName)));
        ImageList.ItemsSource = groupedImages;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DatasetViewModel.SelectedImage))
            {
                Dispatcher.Invoke(() =>
                {
                    ImageList.SelectedItem = vm.SelectedImage;
                    if (vm.SelectedImage is not null
                        && ImageList.ItemContainerGenerator.ContainerFromItem(vm.SelectedImage) is null)
                    {
                        ImageList.ScrollIntoView(vm.SelectedImage);
                    }
                    PositionText.Text = vm.PositionText;
                    PrevButton.IsEnabled = vm.HasPrevious;
                    NextButton.IsEnabled = vm.HasNext;
                });
            }
            if (e.PropertyName is nameof(DatasetViewModel.CanLoadMore) or nameof(DatasetViewModel.IsLoading))
                Dispatcher.Invoke(() =>
                {
                    LoadMoreButton.IsEnabled = vm.CanLoadMore && !vm.IsLoading;
                    LoadMoreButton.Content = vm.IsLoading ? "..." : "+500";
                });
        };

        vm.Images.CollectionChanged += (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
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
            FolderOpenRequested?.Invoke(dialog.FolderName);
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

    private async void LoadMoreOnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) await ViewModel.LoadMoreAsync();
    }

    private async void FilterOnChanged(object sender, EventArgs e)
    {
        if (ViewModel?.IsIndexed != true) return;
        filterDelay?.Cancel();
        filterDelay = new CancellationTokenSource();
        try
        {
            await Task.Delay(250, filterDelay.Token);
            var month = MonthFilter.SelectedItem as string;
            if (MonthFilter.SelectedIndex <= 0) month = null;
            AnnotationWorkflowStatus? status = null;
            if (StatusFilter.SelectedItem is ComboBoxItem { Tag: string tag } &&
                Enum.TryParse<AnnotationWorkflowStatus>(tag, out var parsed)) status = parsed;
            await ViewModel.ApplyFilterAsync(new DatasetIndexFilter(SearchBox.Text, month, status));
        }
        catch (OperationCanceledException) { }
    }
}
