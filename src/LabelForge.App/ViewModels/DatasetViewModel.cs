using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using LabelForge.Persistence;
using LabelForge.Core;

namespace LabelForge.App.ViewModels;

public sealed class ResettableObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

public sealed class DatasetImageEntry : INotifyPropertyChanged
{
    private bool hasAnnotations;

    public string FilePath { get; init; } = string.Empty;
    public long ImageId { get; init; }
    public AnnotationWorkflowStatus WorkflowStatus { get; set; }
    public DatasetSplit Split { get; set; }
    public ImageQualityStatus QualityStatus { get; set; } = ImageQualityStatus.Usable;
    public string TaskName { get; init; } = "Képek";
    public string FileName => Path.GetFileName(FilePath);

    public bool HasAnnotations
    {
        get => hasAnnotations;
        set { hasAnnotations = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class DatasetViewModel : INotifyPropertyChanged
{
    private DatasetImageEntry? selectedImage;
    private int currentIndex = -1;
    private string? folderPath;
    private Func<string, bool>? hasAnnotations;
    private DatasetIndex? index;
    private DatasetIndexFilter filter = new();
    private int loadedCount;
    private const int PageSize = 500;
    private bool canLoadMore;
    private bool isLoading;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<DatasetImageEntry>? ImageNavigated;

    public ResettableObservableCollection<DatasetImageEntry> Images { get; } = [];
    public ObservableCollection<string> Months { get; } = [];
    public bool IsIndexed => index is not null;
    public bool CanLoadMore { get => canLoadMore; private set { canLoadMore = value; OnPropertyChanged(); } }
    public bool IsLoading { get => isLoading; private set { isLoading = value; OnPropertyChanged(); } }

    public DatasetImageEntry? SelectedImage
    {
        get => selectedImage;
        set
        {
            selectedImage = value;
            currentIndex = value is null ? -1 : Images.IndexOf(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(PositionText));
            OnPropertyChanged(nameof(HasPrevious));
            OnPropertyChanged(nameof(HasNext));
        }
    }

    public string? FolderPath
    {
        get => folderPath;
        private set { folderPath = value; OnPropertyChanged(); }
    }

    public string PositionText => Images.Count == 0 ? "—"
        : currentIndex < 0 ? $"? / {Images.Count}"
        : $"{currentIndex + 1} / {Images.Count}";

    public bool HasPrevious => currentIndex > 0;
    public bool HasNext => currentIndex >= 0 && currentIndex < Images.Count - 1;

    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".webp"];

    public void LoadFolder(string path, Func<string, bool>? annotationProbe = null)
    {
        FolderPath = path;
        hasAnnotations = annotationProbe;
        Images.Clear();

        var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var entry = new DatasetImageEntry
            {
                FilePath = file,
                TaskName = "Képek",
                HasAnnotations = HasAnnotationSidecar(file)
            };
            Images.Add(entry);
        }

        if (Images.Count > 0)
        {
            NavigateTo(0);
        }

        OnPropertyChanged(nameof(PositionText));
    }

    public void NavigatePrev()
    {
        if (HasPrevious)
        {
            NavigateTo(currentIndex - 1);
        }
    }

    public void NavigateNext()
    {
        if (HasNext)
        {
            NavigateTo(currentIndex + 1);
        }
    }

    public void RefreshAnnotationState(string imagePath)
    {
        var entry = Images.FirstOrDefault(e => string.Equals(e.FilePath, imagePath, StringComparison.OrdinalIgnoreCase));
        if (entry is not null)
        {
            entry.HasAnnotations = HasAnnotationSidecar(imagePath);
        }
    }

    public async Task LoadFolderAsync(string path, IAnnotationStorePlugin plugin, IProgress<int>? progress = null)
    {
        FolderPath = path;
        hasAnnotations = plugin.HasAnnotations;
        var entries = await Task.Run(() =>
        {
            var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Where(plugin.IsDatasetImage)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var result = new DatasetImageEntry[files.Length];
            for (var i = 0; i < files.Length; i++)
            {
                var group = plugin.GetDatasetGroup(path, files[i]);
                result[i] = new DatasetImageEntry
                {
                    FilePath = files[i],
                    TaskName = group?.Task ?? "Képek",
                    HasAnnotations = plugin.HasAnnotations(files[i])
                };
                if (i % 50 == 0 || i == files.Length - 1)
                    progress?.Report((i + 1) * 100 / Math.Max(1, files.Length));
            }
            return result;
        });
        Images.ReplaceAll(entries);
        if (Images.Count > 0) NavigateTo(0);
        OnPropertyChanged(nameof(PositionText));
    }

    public void LoadDataset(string datasetPath, IEnumerable<DatasetImageEntry> entries)
    {
        FolderPath = Path.GetDirectoryName(datasetPath);
        hasAnnotations = null;
        Images.ReplaceAll(entries);
        if (Images.Count > 0) NavigateTo(0);
        OnPropertyChanged(nameof(PositionText));
    }

    public async Task LoadIndexedDatasetAsync(string datasetPath, DatasetIndex datasetIndex)
    {
        FolderPath = Path.GetDirectoryName(datasetPath);
        index = datasetIndex;
        hasAnnotations = null;
        Months.Clear();
        Months.Add("Minden honap");
        foreach (var month in await index.GetMonthsAsync()) Months.Add(month);
        await ApplyFilterAsync(new DatasetIndexFilter());
        OnPropertyChanged(nameof(IsIndexed));
    }

    public async Task ApplyFilterAsync(DatasetIndexFilter newFilter)
    {
        if (index is null) return;
        filter = newFilter;
        loadedCount = 0;
        Images.ReplaceAll([]);
        SelectedImage = null;
        await LoadMoreAsync();
    }

    public async Task LoadMoreAsync()
    {
        if (index is null || IsLoading) return;
        IsLoading = true;
        try
        {
            var page = await index.QueryAsync(filter, loadedCount, PageSize);
            foreach (var image in page)
                Images.Add(new DatasetImageEntry
                {
                    ImageId = image.ImageId,
                    FilePath = image.FilePath,
                    TaskName = image.Month,
                    HasAnnotations = image.HasAnnotations,
                    WorkflowStatus = image.Status,
                    Split = image.Split,
                    QualityStatus = image.Quality
                });
            loadedCount += page.Count;
            CanLoadMore = page.Count == PageSize;
            if (SelectedImage is null && Images.Count > 0) NavigateTo(0);
            OnPropertyChanged(nameof(PositionText));
        }
        finally { IsLoading = false; }
    }

    private bool HasAnnotationSidecar(string imagePath)
    {
        if (hasAnnotations is not null)
        {
            return hasAnnotations(imagePath);
        }

        if (File.Exists(Path.ChangeExtension(imagePath, ".json")))
        {
            return true;
        }

        var directory = Path.GetDirectoryName(imagePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var fileName = Path.GetFileName(imagePath);
        return File.Exists(Path.Combine(directory, $"woodlogs_{fileName}.txt"))
            || File.Exists(Path.Combine(directory, $"antiwoodlogs_{fileName}.txt"));
    }

    private void NavigateTo(int index)
    {
        if (index < 0 || index >= Images.Count)
        {
            return;
        }

        SelectedImage = Images[index];
        ImageNavigated?.Invoke(this, Images[index]);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
