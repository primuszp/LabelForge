using System.Text.Json;
using System.Text.Json.Serialization;
using LabelForge.Core;

namespace LabelForge.Persistence;

public sealed record CocoDatasetImage(long ImageId, string FilePath, bool HasAnnotations, string Group,
    DateTimeOffset? CaptureDate, AnnotationWorkflowStatus Status, DatasetSplit Split, ImageQualityStatus Quality);

public sealed class CocoDatasetStore : IAnnotationStorePlugin, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private CocoRoot? root;
    private string? datasetPath;
    private string? datasetDirectory;
    private readonly Dictionary<string, CocoImage> imagesByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, List<CocoAnnotation>> annotationsByImage = [];
    private readonly Dictionary<long, CocoCategory> categoriesById = [];
    private long nextAnnotationId;

    public string Id => "coco-native";
    public string DisplayName => "COCO dataset";
    public string Description => "Natív COCO bbox és polygon dataset.";
    public IReadOnlyList<CocoDatasetImage> Images { get; private set; } = [];
    public IReadOnlyList<string> CategoryNames => root?.Categories.Select(c => c.Name).ToArray() ?? [];
    public bool IsDirty { get; private set; }
    public DatasetIndex? Index { get; private set; }

    public async Task OpenAsync(string path, IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(5);
        await using var stream = File.OpenRead(path);
        root = await JsonSerializer.DeserializeAsync<CocoRoot>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("A COCO dataset nem olvasható.");
        datasetPath = path;
        datasetDirectory = Path.GetDirectoryName(path)!;
        progress?.Report(65);

        categoriesById.Clear();
        foreach (var category in root.Categories) categoriesById[category.Id] = category;
        annotationsByImage.Clear();
        foreach (var annotation in root.Annotations)
        {
            if (!annotationsByImage.TryGetValue(annotation.ImageId, out var list))
                annotationsByImage[annotation.ImageId] = list = [];
            list.Add(annotation);
        }
        nextAnnotationId = root.Annotations.Count == 0 ? 1 : root.Annotations.Max(a => a.Id) + 1;

        imagesByPath.Clear();
        var images = new List<CocoDatasetImage>(root.Images.Count);
        foreach (var image in root.Images)
        {
            var resolved = ResolveImagePath(image.FileName);
            imagesByPath[resolved] = image;
            var group = ReadString(image.ExtensionData, "camstudio", "source_database") ?? "COCO dataset";
            DateTimeOffset? captureDate = image.DateCaptured is not null && DateTimeOffset.TryParse(image.DateCaptured, out var parsedDate)
                ? parsedDate
                : null;
            var imageAttributes = ReadObject(image.ExtensionData, "attributes");
            var status = ParseEnum(imageAttributes, "annotation_status", AnnotationWorkflowStatus.Pending);
            var split = ParseEnum(imageAttributes, "split", DatasetSplit.Unassigned);
            var quality = ReadBool(imageAttributes, "is_not_readable") ? ImageQualityStatus.Unreadable
                : ReadBool(imageAttributes, "is_empty") ? ImageQualityStatus.Empty
                : ParseEnum(imageAttributes, "quality_status", ImageQualityStatus.Usable);
            images.Add(new CocoDatasetImage(image.Id, resolved, annotationsByImage.ContainsKey(image.Id), group,
                captureDate, status, split, quality));
        }
        Images = images;
        if (Index is not null) await Index.DisposeAsync();
        Index = await DatasetIndex.OpenAsync(path + ".lfindex", cancellationToken);
        await Index.RebuildAsync(Images, cancellationToken);
        progress?.Report(100);
    }

    public bool HasAnnotations(string imagePath) =>
        imagesByPath.TryGetValue(Path.GetFullPath(imagePath), out var image)
        && annotationsByImage.ContainsKey(image.Id);

    public async Task<ImageDocument?> LoadAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        if (!imagesByPath.TryGetValue(Path.GetFullPath(imagePath), out var image)) return null;
        var document = new ImageDocument
        {
            Image = new ImageInfo(imagePath, image.Width, image.Height),
            AnnotationFilePath = datasetPath
        };
        document.Attributes["source_format"] = "COCO";
        document.Attributes["coco_image_id"] = image.Id.ToString();
        CopyExtensionAttributes(image.ExtensionData, document.Attributes, "coco.image");
        document.CaptureDate = image.DateCaptured is not null && DateTimeOffset.TryParse(image.DateCaptured, out var captureDate) ? captureDate : null;
        var imageAttributes = ReadObject(image.ExtensionData, "attributes");
        document.WorkflowStatus = ParseEnum(imageAttributes, "annotation_status", AnnotationWorkflowStatus.Pending);
        document.Split = ParseEnum(imageAttributes, "split", DatasetSplit.Unassigned);
        document.QualityStatus = ReadBool(imageAttributes, "is_not_readable") ? ImageQualityStatus.Unreadable
            : ReadBool(imageAttributes, "is_empty") ? ImageQualityStatus.Empty : ImageQualityStatus.Usable;

        if (annotationsByImage.TryGetValue(image.Id, out var annotations))
        {
            foreach (var source in annotations)
            {
                var shape = ToShape(source);
                if (shape is null) continue;
                var annotation = new Annotation
                {
                    Label = categoriesById.GetValueOrDefault(source.CategoryId)?.Name ?? $"category-{source.CategoryId}",
                    Shape = shape,
                    Crowd = source.IsCrowd != 0
                };
                annotation.Attributes["coco_id"] = source.Id.ToString();
                annotation.Attributes["coco_category_id"] = source.CategoryId.ToString();
                if (source.Attributes is { } attributes) CopyJsonObject(attributes, annotation.Attributes, string.Empty);
                annotation.WorkflowStatus = ParseEnum(source.Attributes, "annotation_status", AnnotationWorkflowStatus.Pending);
                annotation.Source = ParseEnum(source.Attributes, "annotation_source", AnnotationSourceKind.Original);
                annotation.Reviewer = ReadString(source.Attributes, "reviewer");
                annotation.ModelName = ReadString(source.Attributes, "model_name");
                annotation.ModelVersion = ReadString(source.Attributes, "model_version");
                CopyExtensionAttributes(source.ExtensionData, annotation.Attributes, "coco.annotation");
                document.Annotations.Add(annotation);
            }
        }
        document.IsDirty = false;
        return document;
    }

    public async Task SaveAsync(ImageDocument document, CancellationToken cancellationToken = default)
    {
        if (root is null || datasetPath is null || document.Image is null) return;
        if (!imagesByPath.TryGetValue(Path.GetFullPath(document.Image.FilePath), out var image)) return;

        image.DateCaptured = document.CaptureDate?.ToString("O");
        image.ExtensionData ??= [];
        image.ExtensionData["attributes"] = MergeAttributes(
            ReadObject(image.ExtensionData, "attributes"),
            new Dictionary<string, string>
            {
                ["annotation_status"] = document.WorkflowStatus.ToString(),
                ["split"] = document.Split.ToString(),
                ["quality_status"] = document.QualityStatus.ToString(),
                ["reviewer"] = document.Reviewer ?? string.Empty,
                ["reviewed_at"] = document.ReviewedAt?.ToString("O") ?? string.Empty
            });

        var oldById = root.Annotations.Where(a => a.ImageId == image.Id).ToDictionary(a => a.Id);
        if (Index is not null)
            foreach (var source in document.Annotations)
            {
                source.Revision = Math.Max(1, source.Revision + (source.Attributes.ContainsKey("coco_id") ? 1 : 0));
                var snapshot = JsonSerializer.Serialize(new
                {
                    source.Label,
                    source.WorkflowStatus,
                    source.Source,
                    source.Revision,
                    Points = source.Shape.Points
                });
                await Index.RecordRevisionAsync(image.Id, source.Id, source.Revision, snapshot, cancellationToken);
            }
        root.Annotations.RemoveAll(a => a.ImageId == image.Id);
        var categoryByName = root.Categories.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var source in document.Annotations)
        {
            if (!categoryByName.TryGetValue(source.Label, out var category))
            {
                category = new CocoCategory { Id = root.Categories.Count == 0 ? 1 : root.Categories.Max(c => c.Id) + 1, Name = source.Label };
                root.Categories.Add(category);
                categoryByName[source.Label] = category;
                categoriesById[category.Id] = category;
            }
            var bbox = GetBbox(source.Shape.Points);
            var annotationId = source.Attributes.TryGetValue("coco_id", out var idText) && long.TryParse(idText, out var id) ? id : nextAnnotationId++;
            oldById.TryGetValue(annotationId, out var oldAnnotation);
            var annotation = new CocoAnnotation
            {
                Id = annotationId,
                ImageId = image.Id,
                CategoryId = category.Id,
                Bbox = bbox,
                Area = bbox[2] * bbox[3],
                IsCrowd = source.Crowd ? 1 : 0,
                Segmentation = BuildSegmentation(source.Shape, oldAnnotation?.Segmentation),
                Attributes = MergeAttributes(oldAnnotation?.Attributes, BuildAnnotationAttributes(source)),
                ExtensionData = oldAnnotation?.ExtensionData
            };
            root.Annotations.Add(annotation);
        }
        ReindexAnnotations(image.Id);
        if (Index is not null)
            await Index.UpdateWorkflowAsync(image.Id, document.WorkflowStatus, document.Reviewer, cancellationToken);
        IsDirty = true;
        document.IsDirty = false;
    }

    private static IReadOnlyDictionary<string, string> BuildAnnotationAttributes(Annotation annotation)
    {
        var values = new Dictionary<string, string>(annotation.Attributes, StringComparer.OrdinalIgnoreCase)
        {
            ["annotation_status"] = annotation.WorkflowStatus.ToString(),
            ["annotation_source"] = annotation.Source.ToString(),
            ["revision"] = annotation.Revision.ToString(),
            ["reviewer"] = annotation.Reviewer ?? string.Empty,
            ["reviewed_at"] = annotation.ReviewedAt?.ToString("O") ?? string.Empty,
            ["model_name"] = annotation.ModelName ?? string.Empty,
            ["model_version"] = annotation.ModelVersion ?? string.Empty
        };
        if (annotation.ParentAnnotationId is { } parentId) values["parent_annotation_id"] = parentId.ToString();
        return values;
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (!IsDirty || root is null || datasetPath is null) return;
        var tempPath = datasetPath + ".tmp";
        await using (var stream = File.Create(tempPath))
            await JsonSerializer.SerializeAsync(stream, root, JsonOptions, cancellationToken);
        File.Move(tempPath, datasetPath, true);
        IsDirty = false;
    }

    public void Dispose()
    {
        Index?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Index = null;
    }

    private void ReindexAnnotations(long imageId)
    {
        var list = root!.Annotations.Where(a => a.ImageId == imageId).ToList();
        if (list.Count == 0) annotationsByImage.Remove(imageId); else annotationsByImage[imageId] = list;
    }

    private string ResolveImagePath(string fileName)
    {
        var normalized = fileName.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.IsPathRooted(normalized) ? normalized : Path.Combine(datasetDirectory!, normalized));
    }

    private static AnnotationShape? ToShape(CocoAnnotation annotation)
    {
        if (annotation.Segmentation.ValueKind == JsonValueKind.Array && annotation.Segmentation.GetArrayLength() > 0)
        {
            var first = annotation.Segmentation[0];
            if (first.ValueKind == JsonValueKind.Array && first.GetArrayLength() >= 6)
            {
                var polygon = new PolygonShape();
                var values = first.EnumerateArray().Select(v => v.GetDouble()).ToArray();
                for (var i = 0; i + 1 < values.Length; i += 2) polygon.Vertices.Add(new Point2D(values[i], values[i + 1]));
                return polygon;
            }
        }
        return annotation.Bbox.Count >= 4
            ? new RectangleShape { X = annotation.Bbox[0], Y = annotation.Bbox[1], Width = annotation.Bbox[2], Height = annotation.Bbox[3] }
            : null;
    }

    private static List<double> GetBbox(IReadOnlyList<Point2D> points) => points.Count == 0 ? [0, 0, 0, 0]
        : [points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X) - points.Min(p => p.X), points.Max(p => p.Y) - points.Min(p => p.Y)];

    private static JsonElement BuildSegmentation(AnnotationShape shape, JsonElement? original)
    {
        if (shape is not PolygonShape)
            return original?.Clone() ?? JsonSerializer.SerializeToElement(Array.Empty<double[]>());
        var current = shape.Points.SelectMany(p => new[] { p.X, p.Y }).ToArray();
        if (original is { ValueKind: JsonValueKind.Array } originalArray && originalArray.GetArrayLength() > 0)
        {
            var first = originalArray[0];
            if (first.ValueKind == JsonValueKind.Array)
            {
                var old = first.EnumerateArray().Select(v => v.GetDouble()).ToArray();
                if (old.Length == current.Length && old.Zip(current).All(pair => Math.Abs(pair.First - pair.Second) < 0.001))
                    return originalArray.Clone();
            }
        }
        return JsonSerializer.SerializeToElement(new[] { current });
    }

    private static void CopyJsonObject(JsonElement element, Dictionary<string, string> target, string prefix)
    {
        if (element.ValueKind != JsonValueKind.Object) return;
        foreach (var property in element.EnumerateObject())
            target[string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}"] = property.Value.ToString();
    }

    private static JsonElement MergeAttributes(JsonElement? original, IReadOnlyDictionary<string, string> edited)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (original is { ValueKind: JsonValueKind.Object } originalObject)
            foreach (var property in originalObject.EnumerateObject()) values[property.Name] = property.Value.Clone();
        foreach (var item in edited.Where(kv => !kv.Key.StartsWith("coco", StringComparison.OrdinalIgnoreCase)))
        {
            if (values.TryGetValue(item.Key, out var old) && old.ToString() == item.Value) continue;
            values[item.Key] = JsonSerializer.SerializeToElement(item.Value);
        }
        return JsonSerializer.SerializeToElement(values);
    }

    private static void CopyExtensionAttributes(Dictionary<string, JsonElement>? source, Dictionary<string, string> target, string prefix)
    {
        if (source is null) return;
        foreach (var item in source) target[$"{prefix}.{item.Key}"] = item.Value.ToString();
    }

    private static string? ReadString(Dictionary<string, JsonElement>? source, string objectName, string propertyName)
    {
        if (source is null || !source.TryGetValue(objectName, out var value) || value.ValueKind != JsonValueKind.Object) return null;
        return value.TryGetProperty(propertyName, out var property) ? property.GetString() : null;
    }

    private static JsonElement? ReadObject(Dictionary<string, JsonElement>? source, string name) =>
        source is not null && source.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    private static bool ReadBool(JsonElement? source, string name) => source is { ValueKind: JsonValueKind.Object } value
        && value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.True;

    private static string? ReadString(JsonElement? source, string name) => source is { ValueKind: JsonValueKind.Object } value
        && value.TryGetProperty(name, out var property) ? property.ToString() : null;

    private static T ParseEnum<T>(JsonElement? source, string name, T fallback) where T : struct, Enum =>
        ReadString(source, name) is { } text && Enum.TryParse<T>(text, true, out var result) ? result : fallback;

    private sealed class CocoRoot
    {
        [JsonPropertyName("images")] public List<CocoImage> Images { get; set; } = [];
        [JsonPropertyName("annotations")] public List<CocoAnnotation> Annotations { get; set; } = [];
        [JsonPropertyName("categories")] public List<CocoCategory> Categories { get; set; } = [];
        [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }
    private sealed class CocoImage
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("file_name")] public string FileName { get; set; } = string.Empty;
        [JsonPropertyName("width")] public int Width { get; set; }
        [JsonPropertyName("height")] public int Height { get; set; }
        [JsonPropertyName("date_captured")] public string? DateCaptured { get; set; }
        [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }
    private sealed class CocoAnnotation
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("image_id")] public long ImageId { get; set; }
        [JsonPropertyName("category_id")] public long CategoryId { get; set; }
        [JsonPropertyName("bbox")] public List<double> Bbox { get; set; } = [];
        [JsonPropertyName("segmentation")] public JsonElement Segmentation { get; set; }
        [JsonPropertyName("area")] public double Area { get; set; }
        [JsonPropertyName("iscrowd")] public int IsCrowd { get; set; }
        [JsonPropertyName("attributes")] public JsonElement? Attributes { get; set; }
        [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }
    private sealed class CocoCategory
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("supercategory")] public string SuperCategory { get; set; } = "object";
        [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }
}
