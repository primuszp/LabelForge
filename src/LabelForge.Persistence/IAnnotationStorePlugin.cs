using LabelForge.Core;

namespace LabelForge.Persistence;

public sealed record DatasetImageGroup(string Task, string Scene);

public interface IAnnotationStorePlugin
{
    string Id { get; }
    string DisplayName { get; }
    string Description { get; }

    bool CanHandleDataset(string folderPath) => false;
    bool IsDatasetImage(string imagePath) => true;
    DatasetImageGroup? GetDatasetGroup(string datasetRoot, string imagePath) => null;
    bool HasAnnotations(string imagePath);
    Task<ImageDocument?> LoadAsync(string imagePath, CancellationToken cancellationToken = default);
    Task SaveAsync(ImageDocument document, CancellationToken cancellationToken = default);
}
