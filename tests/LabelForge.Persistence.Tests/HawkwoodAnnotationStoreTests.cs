using LabelForge.Core;
using LabelForge.Persistence;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LabelForge.Persistence.Tests;

public sealed class HawkwoodAnnotationStoreTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public HawkwoodAnnotationStoreTests()
    {
        Directory.CreateDirectory(tempDirectory);
    }

    [Fact]
    public async Task LoadAsync_ReadsWoodlogAndAntiWoodlogSidecars()
    {
        var imagePath = Path.Combine(tempDirectory, "Polter001.png");
        File.WriteAllText(imagePath, string.Empty);
        await File.WriteAllLinesAsync(Path.Combine(tempDirectory, "woodlogs_Polter001.png.txt"),
        [
            "504;128;66;66",
            "440;130;63;63"
        ]);
        await File.WriteAllLinesAsync(Path.Combine(tempDirectory, "antiwoodlogs_Polter001.png.txt"),
        [
            "749;74;52;52"
        ]);

        var store = new HawkwoodAnnotationStore();
        var document = await store.LoadAsync(imagePath);

        Assert.NotNull(document);
        Assert.Equal(3, document.Annotations.Count);
        Assert.Equal("HAWKwood", document.Attributes["source_format"]);
        Assert.Equal(["woodlog", "woodlog", "antiwoodlog"], document.Annotations.Select(a => a.Label));
        var first = Assert.IsType<RectangleShape>(document.Annotations[0].Shape);
        Assert.Equal(504, first.X);
        Assert.Equal(128, first.Y);
        Assert.Equal(66, first.Width);
        Assert.Equal(66, first.Height);
    }

    [Fact]
    public async Task SaveAsync_WritesOriginalSidecarFormat()
    {
        var imagePath = Path.Combine(tempDirectory, "Polter001.png");
        File.WriteAllText(imagePath, string.Empty);
        var document = new ImageDocument
        {
            Image = new LabelForge.Core.ImageInfo(imagePath, 800, 600)
        };
        document.Annotations.Add(new Annotation
        {
            Label = "woodlog",
            Shape = new RectangleShape { X = 10, Y = 20, Width = 30, Height = 40 }
        });
        document.Annotations.Add(new Annotation
        {
            Label = "antiwoodlog",
            Shape = new RectangleShape { X = 50, Y = 60, Width = 70, Height = 80 }
        });

        var store = new HawkwoodAnnotationStore();
        await store.SaveAsync(document);

        Assert.Equal(["10;20;30;40"], await File.ReadAllLinesAsync(Path.Combine(tempDirectory, "woodlogs_Polter001.png.txt")));
        Assert.Equal(["50;60;70;80"], await File.ReadAllLinesAsync(Path.Combine(tempDirectory, "antiwoodlogs_Polter001.png.txt")));
        Assert.False(document.IsDirty);
        Assert.Equal(imagePath, document.AnnotationFilePath);
    }

    [Fact]
    public async Task Mask_RoundTripKeepsSeparateComponents()
    {
        var imagePath = Path.Combine(tempDirectory, "sample.JPG");
        File.WriteAllText(imagePath, string.Empty);
        var maskPath = Path.Combine(tempDirectory, "sample_mask2.png");
        using (var mask = new Image<L8>(32, 24, new L8(0)))
        {
            for (var y = 3; y < 10; y++) for (var x = 2; x < 9; x++) mask[x, y] = new L8(255);
            for (var y = 12; y < 21; y++) for (var x = 19; x < 29; x++) mask[x, y] = new L8(255);
            await mask.SaveAsPngAsync(maskPath);
        }

        var store = new HawkwoodAnnotationStore();
        var document = await store.LoadAsync(imagePath);

        Assert.NotNull(document);
        Assert.Equal(2, document.Annotations.Count(a => a.Label == "woodlog-mask"));
        Assert.All(document.Annotations, annotation => Assert.IsType<PolygonShape>(annotation.Shape));

        await store.SaveAsync(document);
        using var saved = await Image.LoadAsync<L8>(maskPath);
        Assert.Equal(255, saved[4, 5].PackedValue);
        Assert.Equal(255, saved[24, 16].PackedValue);
        Assert.Equal(0, saved[14, 11].PackedValue);
    }

    [Fact]
    public void DatasetGroups_FollowHawkwoodBenchmarkTasks()
    {
        var s2Directory = Path.Combine(tempDirectory, "Single Image Benchmark", "S.1 and S.2 real");
        Directory.CreateDirectory(s2Directory);
        var s2Image = Path.Combine(s2Directory, "sample.JPG");
        File.WriteAllText(s2Image, string.Empty);
        using (var mask = new Image<L8>(4, 4, new L8(0))) mask.SaveAsPng(Path.Combine(s2Directory, "sample_mask2.png"));

        var multiDirectory = Path.Combine(tempDirectory, "Multi Image Benchmark", "real", "pile", "small_overlap", "Test 1");
        Directory.CreateDirectory(multiDirectory);
        File.WriteAllText(Path.Combine(tempDirectory, "Multi Image Benchmark", "real", "pile", "Info.txt"),
            "Number of logs:\t12\nContour volume:\t8.5m^3");
        var multiImage = Path.Combine(multiDirectory, "IMG_0001.JPG");
        File.WriteAllText(multiImage, string.Empty);

        var store = new HawkwoodAnnotationStore();
        var s2 = store.GetDatasetGroup(tempDirectory, s2Image);
        var multi = store.GetDatasetGroup(tempDirectory, multiImage);

        Assert.Contains("S.1 detektálás", s2!.Task);
        Assert.Contains("S.2 rönkvég-szegmentálás", s2.Task);
        Assert.Equal("M.1 + M.3 - Többképes felmérés", multi!.Task);
        Assert.Contains("kis átfedés - panoráma", multi.Scene);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
