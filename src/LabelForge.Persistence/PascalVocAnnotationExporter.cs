using System.Text;
using System.Xml;
using LabelForge.Core;

namespace LabelForge.Persistence;

/// <summary>
/// Exports annotations in Pascal VOC XML format.
/// One .xml file per image document.
/// </summary>
public sealed class PascalVocAnnotationExporter : IAnnotationExporter
{
    public async Task<string> ExportAsync(ImageDocument document, string outputFolder,
        IReadOnlyList<string> orderedLabels, CancellationToken cancellationToken = default)
    {
        if (document.Image is null)
        {
            throw new InvalidOperationException("No image loaded.");
        }

        var outputPath = Path.Combine(outputFolder,
            Path.GetFileNameWithoutExtension(document.Image.FilePath) + ".xml");

        var xml = BuildXml(document);
        await File.WriteAllTextAsync(outputPath, xml, Encoding.UTF8, cancellationToken);
        return outputPath;
    }

    private static string BuildXml(ImageDocument document)
    {
        var img = document.Image!;
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8 };

        using var writer = XmlWriter.Create(sb, settings);
        writer.WriteStartElement("annotation");

        writer.WriteElementString("folder", Path.GetDirectoryName(img.FilePath) ?? string.Empty);
        writer.WriteElementString("filename", Path.GetFileName(img.FilePath));
        writer.WriteElementString("path", img.FilePath);

        writer.WriteStartElement("source");
        writer.WriteElementString("database", "LabelForge");
        writer.WriteEndElement();

        writer.WriteStartElement("size");
        writer.WriteElementString("width", img.Width.ToString());
        writer.WriteElementString("height", img.Height.ToString());
        writer.WriteElementString("depth", "3");
        writer.WriteEndElement();

        writer.WriteElementString("segmented", "0");

        foreach (var annotation in document.Annotations.Where(a => a.IsVisible))
        {
            var pts = annotation.Shape.Points;
            if (!pts.Any()) continue;

            var xmin = pts.Min(p => p.X);
            var ymin = pts.Min(p => p.Y);
            var xmax = pts.Max(p => p.X);
            var ymax = pts.Max(p => p.Y);

            writer.WriteStartElement("object");
            writer.WriteElementString("name", annotation.Label);
            writer.WriteElementString("pose", "Unspecified");
            writer.WriteElementString("truncated", "0");
            writer.WriteElementString("difficult", "0");
            writer.WriteStartElement("bndbox");
            writer.WriteElementString("xmin", ((int)Math.Round(xmin)).ToString());
            writer.WriteElementString("ymin", ((int)Math.Round(ymin)).ToString());
            writer.WriteElementString("xmax", ((int)Math.Round(xmax)).ToString());
            writer.WriteElementString("ymax", ((int)Math.Round(ymax)).ToString());
            writer.WriteEndElement(); // bndbox
            writer.WriteEndElement(); // object
        }

        writer.WriteEndElement(); // annotation
        writer.Flush();
        return sb.ToString();
    }
}
