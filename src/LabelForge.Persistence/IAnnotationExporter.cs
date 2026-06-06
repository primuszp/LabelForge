using LabelForge.Core;

namespace LabelForge.Persistence;

public interface IAnnotationExporter
{
    /// <summary>Export a single image document. Returns the written file path.</summary>
    Task<string> ExportAsync(ImageDocument document, string outputFolder,
        IReadOnlyList<string> orderedLabels, CancellationToken cancellationToken = default);
}
