using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace LabelForge.App.ViewModels;

public sealed class DatasetImageEntry : INotifyPropertyChanged
{
    private bool hasAnnotations;

    public string FilePath { get; init; } = string.Empty;
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

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<DatasetImageEntry>? ImageNavigated;

    public ObservableCollection<DatasetImageEntry> Images { get; } = [];

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

    public void LoadFolder(string path)
    {
        FolderPath = path;
        Images.Clear();

        var files = Directory.EnumerateFiles(path)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var entry = new DatasetImageEntry
            {
                FilePath = file,
                HasAnnotations = File.Exists(Path.ChangeExtension(file, ".json"))
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
            entry.HasAnnotations = File.Exists(Path.ChangeExtension(imagePath, ".json"));
        }
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
