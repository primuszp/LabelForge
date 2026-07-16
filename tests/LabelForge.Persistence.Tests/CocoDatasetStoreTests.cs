using System.Text.Json;
using LabelForge.Core;
using LabelForge.Persistence;

namespace LabelForge.Persistence.Tests;

public sealed class CocoDatasetStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public CocoDatasetStoreTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task OpenAndSave_PreservesExtensionsAndSupportsBboxAndPolygon()
    {
        File.WriteAllText(Path.Combine(directory, "one.jpg"), string.Empty);
        var path = Path.Combine(directory, "dataset.json");
        await File.WriteAllTextAsync(path, """
        {
          "info":{"description":"test"},
          "images":[{"id":1,"file_name":"one.jpg","width":640,"height":480,"camstudio":{"source_database":"source.db"}}],
          "categories":[{"id":3,"name":"car","supercategory":"road_user"}],
          "annotations":[
            {"id":10,"image_id":1,"category_id":3,"bbox":[10,20,30,40],"area":1200,"iscrowd":0,"segmentation":[],"attributes":{"direction":"in"},"camstudio":{"source_entity_id":"abc"}},
            {"id":11,"image_id":1,"category_id":3,"bbox":[0,0,20,20],"area":200,"iscrowd":0,"segmentation":[[0,0,20,0,10,20]]}
          ],
          "camstudio":{"schema_version":"1.0"}
        }
        """);

        var store = new CocoDatasetStore();
        await store.OpenAsync(path);
        var document = await store.LoadAsync(Path.Combine(directory, "one.jpg"));

        Assert.NotNull(document);
        Assert.Equal(2, document.Annotations.Count);
        Assert.IsType<RectangleShape>(document.Annotations[0].Shape);
        Assert.IsType<PolygonShape>(document.Annotations[1].Shape);
        Assert.Equal("in", document.Annotations[0].Attributes["direction"]);

        document.Annotations[0].Attributes["direction"] = "out";
        await store.SaveAsync(document);
        Assert.True(store.IsDirty);
        await store.FlushAsync();

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.Equal("1.0", json.RootElement.GetProperty("camstudio").GetProperty("schema_version").GetString());
        var first = json.RootElement.GetProperty("annotations").EnumerateArray().First(a => a.GetProperty("id").GetInt64() == 10);
        Assert.Equal("abc", first.GetProperty("camstudio").GetProperty("source_entity_id").GetString());
        Assert.Equal("out", first.GetProperty("attributes").GetProperty("direction").GetString());
        store.Dispose();
    }

    public void Dispose() => Directory.Delete(directory, true);
}
