namespace LabelForge.Persistence;

public sealed class AnnotationStorePluginRegistry
{
    public AnnotationStorePluginRegistry(IEnumerable<IAnnotationStorePlugin> plugins)
    {
        Plugins = plugins.ToArray();
    }

    public IReadOnlyList<IAnnotationStorePlugin> Plugins { get; }

    public IAnnotationStorePlugin? FindById(string id) =>
        Plugins.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    public IAnnotationStorePlugin? DetectForFolder(string folderPath) =>
        Plugins.FirstOrDefault(p => p.CanHandleDataset(folderPath));

    public static AnnotationStorePluginRegistry CreateDefault() =>
        new(
        [
            new LabelMeAnnotationStorePlugin(),
            new HawkwoodAnnotationStore()
        ]);
}
