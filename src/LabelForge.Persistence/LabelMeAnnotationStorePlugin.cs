using LabelForge.Core;

namespace LabelForge.Persistence;

public sealed class LabelMeAnnotationStorePlugin : IAnnotationStorePlugin
{
    private readonly LabelMeAnnotationStore store = new();

    public string Id => "labelme";
    public string DisplayName => "LabelMe JSON";
    public string Description => "Kepenkenti .json sidecar fajlok LabelMe/LabelForge formatumban.";

    public bool HasAnnotations(string imagePath) =>
        File.Exists(Path.ChangeExtension(imagePath, ".json"));

    public async Task<ImageDocument?> LoadAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        var jsonPath = Path.ChangeExtension(imagePath, ".json");
        if (!File.Exists(jsonPath))
        {
            return null;
        }

        var document = await store.LoadAsync(jsonPath, cancellationToken);
        document.AnnotationFilePath = jsonPath;
        return document;
    }

    public Task SaveAsync(ImageDocument document, CancellationToken cancellationToken = default)
    {
        if (document.Image is null)
        {
            throw new InvalidOperationException("No image is loaded.");
        }

        var path = document.AnnotationFilePath;
        if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            path = Path.ChangeExtension(document.Image.FilePath, ".json");
        }

        return store.SaveAsync(document, path, cancellationToken);
    }
}
