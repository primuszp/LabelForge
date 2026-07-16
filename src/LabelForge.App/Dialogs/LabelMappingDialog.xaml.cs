using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using LabelForge.App.Services;

namespace LabelForge.App.Dialogs;

public partial class LabelMappingDialog : Window
{
    private readonly LabelMappingService service;
    private readonly Dictionary<string, IReadOnlyList<string>> sources;
    private List<MappingRow> rows = [];

    public LabelMappingDialog(LabelMappingService service, Dictionary<string, IReadOnlyList<string>> sources, Window owner)
    {
        InitializeComponent(); Owner = owner; this.service = service; this.sources = sources;
        ProfileBox.ItemsSource = sources.Keys; TargetColumn.ItemsSource = new[] { "(nincs)" }.Concat(service.ProjectLabels).ToArray();
        ProfileBox.SelectedIndex = sources.Count > 0 ? 0 : -1;
    }

    private void ProfileOnChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ProfileBox.SelectedItem is not string profile || !sources.TryGetValue(profile, out var labels)) return;
        var values = service.GetProfile(profile, labels);
        rows = labels.Distinct(StringComparer.OrdinalIgnoreCase).Select(source => new MappingRow
        { Source = source, Target = values.GetValueOrDefault(source) ?? "(nincs)", Skip = values.ContainsKey(source) && values[source] is null }).ToList();
        MappingGrid.ItemsSource = rows;
    }

    private void AutoMatchOnClick(object sender, RoutedEventArgs e)
    {
        if (ProfileBox.SelectedItem is not string profile) return;
        foreach (var row in rows)
        {
            var match = service.Resolve(profile, row.Source);
            row.Target = match ?? "(nincs)"; row.Skip = match is null;
        }
    }

    private void SaveOnClick(object sender, RoutedEventArgs e)
    {
        if (ProfileBox.SelectedItem is not string profile) return;
        service.SaveProfile(profile, rows.Select(row => new KeyValuePair<string, string?>(row.Source,
            row.Skip || row.Target == "(nincs)" ? null : row.Target)));
        DialogResult = true; Close();
    }

    private sealed class MappingRow : INotifyPropertyChanged
    {
        private string target = "(nincs)"; private bool skip;
        public string Source { get; init; } = string.Empty;
        public string Target { get => target; set { target = value; Changed(); } }
        public bool Skip { get => skip; set { skip = value; Changed(); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    }
}
