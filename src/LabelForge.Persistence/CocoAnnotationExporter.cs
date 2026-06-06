using System.Text.Json;
using System.Text.Json.Serialization;
using LabelForge.Core;

namespace LabelForge.Persistence;

/// <summary>
/// Exports annotations in COCO JSON format.
/// All documents in a batch share a single output file: annotations.json
/// Call ExportBatchAsync for multi-image export.
/// </summary>
public sealed class CocoAnnotationExporter : IAnnotationExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<string> ExportAsync(ImageDocument document, string outputFolder,
        IReadOnlyList<string> orderedLabels, CancellationToken cancellationToken = default)
    {
        await ExportBatchAsync([document], outputFolder, orderedLabels, cancellationToken);
        return Path.Combine(outputFolder, "annotations.json");
    }

    public async Task ExportBatchAsync(IReadOnlyList<ImageDocument> documents, string outputFolder,
        IReadOnlyList<string> orderedLabels, CancellationToken cancellationToken = default)
    {
        var cocoDoc = BuildCoco(documents, orderedLabels);
        var outputPath = Path.Combine(outputFolder, "annotations.json");
        await using var stream = File.Create(outputPath);
        await JsonSerializer.SerializeAsync(stream, cocoDoc, JsonOptions, cancellationToken);
    }

    private static CocoDocument BuildCoco(IReadOnlyList<ImageDocument> documents, IReadOnlyList<string> orderedLabels)
    {
        var categories = orderedLabels.Select((name, i) => new CocoCategory
        {
            Id = i + 1,
            Name = name,
            SuperCategory = "object"
        }).ToList();

        var images = new List<CocoImage>();
        var annotations = new List<CocoAnnotation>();
        var annotId = 1;

        for (var imgIdx = 0; imgIdx < documents.Count; imgIdx++)
        {
            var doc = documents[imgIdx];
            if (doc.Image is null) continue;

            var imgId = imgIdx + 1;
            images.Add(new CocoImage
            {
                Id = imgId,
                FileName = Path.GetFileName(doc.Image.FilePath),
                Width = doc.Image.Width,
                Height = doc.Image.Height
            });

            foreach (var annotation in doc.Annotations.Where(a => a.IsVisible))
            {
                var catId = orderedLabels.ToList().IndexOf(annotation.Label) + 1;
                if (catId <= 0) continue;

                var bbox = GetBbox(annotation);
                var segmentation = GetSegmentation(annotation);

                annotations.Add(new CocoAnnotation
                {
                    Id = annotId++,
                    ImageId = imgId,
                    CategoryId = catId,
                    BBox = bbox,
                    Area = bbox[2] * bbox[3],
                    Segmentation = segmentation,
                    IsCrowd = 0
                });
            }
        }

        return new CocoDocument
        {
            Info = new CocoInfo { Description = "LabelForge export", Version = "1.0", Year = DateTime.Now.Year },
            Categories = categories,
            Images = images,
            Annotations = annotations
        };
    }

    private static List<double> GetBbox(Annotation annotation)
    {
        var pts = annotation.Shape.Points;
        if (!pts.Any()) return [0, 0, 0, 0];
        var minX = pts.Min(p => p.X);
        var minY = pts.Min(p => p.Y);
        var maxX = pts.Max(p => p.X);
        var maxY = pts.Max(p => p.Y);
        return [Math.Round(minX, 2), Math.Round(minY, 2), Math.Round(maxX - minX, 2), Math.Round(maxY - minY, 2)];
    }

    private static List<List<double>> GetSegmentation(Annotation annotation) =>
        [annotation.Shape.Points.SelectMany(p => new[] { Math.Round(p.X, 2), Math.Round(p.Y, 2) }).ToList()];

    private sealed class CocoDocument
    {
        [JsonPropertyName("info")]     public CocoInfo? Info { get; init; }
        [JsonPropertyName("images")]   public List<CocoImage> Images { get; init; } = [];
        [JsonPropertyName("annotations")] public List<CocoAnnotation> Annotations { get; init; } = [];
        [JsonPropertyName("categories")] public List<CocoCategory> Categories { get; init; } = [];
    }

    private sealed class CocoInfo
    {
        [JsonPropertyName("description")] public string Description { get; init; } = string.Empty;
        [JsonPropertyName("version")]     public string Version { get; init; } = "1.0";
        [JsonPropertyName("year")]        public int Year { get; init; }
    }

    private sealed class CocoImage
    {
        [JsonPropertyName("id")]        public int Id { get; init; }
        [JsonPropertyName("file_name")] public string FileName { get; init; } = string.Empty;
        [JsonPropertyName("width")]     public int Width { get; init; }
        [JsonPropertyName("height")]    public int Height { get; init; }
    }

    private sealed class CocoAnnotation
    {
        [JsonPropertyName("id")]          public int Id { get; init; }
        [JsonPropertyName("image_id")]    public int ImageId { get; init; }
        [JsonPropertyName("category_id")] public int CategoryId { get; init; }
        [JsonPropertyName("segmentation")] public List<List<double>> Segmentation { get; init; } = [];
        [JsonPropertyName("bbox")]        public List<double> BBox { get; init; } = [];
        [JsonPropertyName("area")]        public double Area { get; init; }
        [JsonPropertyName("iscrowd")]     public int IsCrowd { get; init; }
    }

    private sealed class CocoCategory
    {
        [JsonPropertyName("id")]            public int Id { get; init; }
        [JsonPropertyName("name")]          public string Name { get; init; } = string.Empty;
        [JsonPropertyName("supercategory")] public string SuperCategory { get; init; } = "object";
    }
}
