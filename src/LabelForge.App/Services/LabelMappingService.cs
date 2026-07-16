using System.Globalization;
using System.Text;
using LabelForge.App.Models;

namespace LabelForge.App.Services;

public sealed class LabelMappingService
{
    private readonly ProjectService projects;
    private readonly LabelClassService labels;
    private Dictionary<string, Dictionary<string, string?>> mappings = new(StringComparer.OrdinalIgnoreCase);

    public LabelMappingService(ProjectService projects, LabelClassService labels)
    {
        this.projects = projects;
        this.labels = labels;
        projects.ProjectChanged += (_, _) => LoadProject();
    }

    public IReadOnlyList<string> ProjectLabels => labels.Classes.Select(label => label.Name).ToArray();

    public string? Resolve(string profile, string source)
    {
        if (mappings.TryGetValue(profile, out var profileMappings) && profileMappings.TryGetValue(source, out var target))
            return string.IsNullOrWhiteSpace(target) ? null : target;
        var normalized = Normalize(source);
        var exact = labels.Classes.Where(label => Normalize(label.Name) == normalized).Select(label => label.Name).ToArray();
        return exact.Length == 1 ? exact[0] : null;
    }

    public Dictionary<string, string?> GetProfile(string profile, IEnumerable<string> sourceLabels)
    {
        if (!mappings.TryGetValue(profile, out var values))
            mappings[profile] = values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sourceLabels.Distinct(StringComparer.OrdinalIgnoreCase))
            if (!values.ContainsKey(source)) values[source] = Resolve(profile, source);
        return values;
    }

    public void SaveProfile(string profile, IEnumerable<KeyValuePair<string, string?>> values)
    {
        mappings[profile] = values.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
        if (projects.Current is not null)
            projects.Current.ModelLabelMappings = mappings.ToDictionary(profileItem => profileItem.Key,
                profileItem => new Dictionary<string, string?>(profileItem.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
    }

    private void LoadProject()
    {
        mappings = projects.Current?.ModelLabelMappings?.ToDictionary(item => item.Key,
            item => new Dictionary<string, string?>(item.Value, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase) ?? new(StringComparer.OrdinalIgnoreCase);
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var character in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(character))
                builder.Append(character);
        return builder.ToString();
    }
}
