using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LabelForge.App.Models;

namespace LabelForge.App.Services;

public sealed class ProjectService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ProjectFile? Current { get; private set; }
    public string? FilePath { get; private set; }
    public bool IsOpen => Current is not null;

    public event EventHandler? ProjectChanged;

    public static ProjectFile CreateEmpty(string name = "Névtelen projekt") =>
        new() { Name = name };

    public void New(string name = "Névtelen projekt")
    {
        Current = CreateEmpty(name);
        FilePath = null;
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> OpenAsync(string path)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var project = await JsonSerializer.DeserializeAsync<ProjectFile>(stream, JsonOptions);
            if (project is null) return false;

            Current = project;
            FilePath = path;
            ProjectChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task SaveAsync(string path, LabelClassService labelService, string? datasetFolder)
    {
        if (Current is null) return;

        Current.DatasetFolder = datasetFolder;
        Current.LabelClasses = labelService.Classes.Select(c => new ProjectLabelClass
        {
            Name = c.Name,
            ColorHex = c.ColorHex,
            HotKey = c.HotKey,
            FillOpacity = c.FillOpacity,
            StrokeThickness = c.StrokeThickness,
            IsVisible = c.IsVisible
        }).ToList();

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, Current, JsonOptions);
        FilePath = path;
    }

    public void ApplyToLabelService(LabelClassService labelService)
    {
        if (Current is null || Current.LabelClasses.Count == 0) return;

        labelService.Classes.Clear();
        foreach (var lc in Current.LabelClasses)
        {
            var cls = labelService.AddRaw(lc.Name, lc.ColorHex, lc.HotKey);
            cls.FillOpacity = lc.FillOpacity;
            cls.StrokeThickness = lc.StrokeThickness;
            cls.IsVisible = lc.IsVisible;
        }
    }

    public void Close()
    {
        Current = null;
        FilePath = null;
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }
}
