using LabelForge.Core;
using LabelForge.Persistence;

namespace LabelForge.Persistence.Tests;

public sealed class LabelMeAnnotationStoreTests
{
    [Fact]
    public async Task SaveAndLoadPreservesPolygonAnnotation()
    {
        var document = new ImageDocument
        {
            Image = new ImageInfo("sample.jpg", 640, 480)
        };
        var polygon = new PolygonShape();
        polygon.Vertices.Add(new Point2D(10, 20));
        polygon.Vertices.Add(new Point2D(30, 40));
        polygon.Vertices.Add(new Point2D(50, 20));
        document.Annotations.Add(new Annotation { Label = "crack", Shape = polygon });

        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        var store = new LabelMeAnnotationStore();

        try
        {
            await store.SaveAsync(document, path);
            var loaded = await store.LoadAsync(path);

            var annotation = Assert.Single(loaded.Annotations);
            Assert.Equal("crack", annotation.Label);
            var loadedPolygon = Assert.IsType<PolygonShape>(annotation.Shape);
            Assert.Equal(3, loadedPolygon.Vertices.Count);
            Assert.Equal(new Point2D(30, 40), loadedPolygon.Vertices[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
