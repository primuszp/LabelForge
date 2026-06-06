using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using LabelForge.App.Models;

namespace LabelForge.App.Services;

public sealed class LabelClassService
{
    private static readonly string SettingsFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LabelForge");

    private static readonly string SettingsFile =
        Path.Combine(SettingsFolder, "label_classes.json");

    private static readonly string[] DefaultColors =
    [
        "#22c55e", "#3b82f6", "#ef4444", "#f59e0b", "#8b5cf6",
        "#06b6d4", "#f97316", "#ec4899", "#10b981", "#6366f1"
    ];

    public ObservableCollection<LabelClass> Classes { get; } = [];

    public LabelClass? ActiveClass { get; private set; }

    public void SetActive(LabelClass? labelClass)
    {
        ActiveClass = labelClass;
    }

    public LabelClass Add(string name)
    {
        var color = DefaultColors[Classes.Count % DefaultColors.Length];
        var hotKey = Classes.Count < 9 ? Classes.Count + 1 : 0;
        var label = new LabelClass { Name = name, ColorHex = color, HotKey = hotKey };
        Classes.Add(label);
        if (ActiveClass is null)
            ActiveClass = label;
        return label;
    }

    /// <summary>Add a label with explicit color and hotkey (used when loading from project file).</summary>
    public LabelClass AddRaw(string name, string colorHex, int hotKey)
    {
        var label = new LabelClass { Name = name, ColorHex = colorHex, HotKey = hotKey };
        Classes.Add(label);
        if (ActiveClass is null)
            ActiveClass = label;
        return label;
    }

    public void Remove(LabelClass label)
    {
        Classes.Remove(label);
        if (ActiveClass == label)
        {
            ActiveClass = Classes.FirstOrDefault();
        }

        RenumberHotKeys();
    }

    public void UpdateAnnotationCounts(IEnumerable<Core.Annotation> annotations)
    {
        var counts = annotations.GroupBy(a => a.Label, StringComparer.OrdinalIgnoreCase)
                                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var label in Classes)
        {
            label.AnnotationCount = counts.TryGetValue(label.Name, out var c) ? c : 0;
        }
    }

    public LabelClass? FindByHotKey(int key) =>
        Classes.FirstOrDefault(c => c.HotKey == key);

    public async Task LoadAsync()
    {
        if (!File.Exists(SettingsFile))
        {
            EnsureDefaults();
            return;
        }

        try
        {
            await using var stream = File.OpenRead(SettingsFile);
            var dtos = await JsonSerializer.DeserializeAsync<List<LabelClassDto>>(stream);
            if (dtos is null || dtos.Count == 0)
            {
                EnsureDefaults();
                return;
            }

            Classes.Clear();
            foreach (var dto in dtos)
            {
                Classes.Add(new LabelClass
                {
                    Name = dto.Name,
                    ColorHex = dto.ColorHex,
                    HotKey = dto.HotKey,
                    FillOpacity = dto.FillOpacity,
                    StrokeThickness = dto.StrokeThickness,
                    IsVisible = dto.IsVisible
                });
            }

            ActiveClass = Classes.FirstOrDefault();
        }
        catch
        {
            EnsureDefaults();
        }
    }

    public async Task SaveAsync()
    {
        Directory.CreateDirectory(SettingsFolder);
        var dtos = Classes.Select(c => new LabelClassDto(c.Name, c.ColorHex, c.HotKey, c.FillOpacity, c.StrokeThickness, c.IsVisible)).ToList();
        await using var stream = File.Create(SettingsFile);
        await JsonSerializer.SerializeAsync(stream, dtos, new JsonSerializerOptions { WriteIndented = true });
    }

    private void EnsureDefaults()
    {
        if (Classes.Count == 0)
        {
            Add("object");
        }

        ActiveClass = Classes.First();
    }

    private void RenumberHotKeys()
    {
        for (var i = 0; i < Classes.Count; i++)
        {
            Classes[i].HotKey = i < 9 ? i + 1 : 0;
        }
    }

    private sealed record LabelClassDto(
        string Name,
        string ColorHex,
        int HotKey,
        double FillOpacity = 0.22,
        double StrokeThickness = 2.0,
        bool IsVisible = true);
}
