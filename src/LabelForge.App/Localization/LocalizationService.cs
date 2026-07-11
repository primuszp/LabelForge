using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace LabelForge.App.Localization;

public static class LocalizationService
{
    private const string Automatic = "auto";
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LabelForge", "language.json");

    public static event EventHandler? LanguageChanged;
    public static string Preference { get; private set; } = Automatic;
    public static string CurrentLanguage { get; private set; } = "en";

    public static void Initialize()
    {
        try
        {
            if (File.Exists(SettingsPath))
                Preference = JsonSerializer.Deserialize<LanguageSettings>(File.ReadAllText(SettingsPath))?.Language ?? Automatic;
        }
        catch
        {
            Preference = Automatic;
        }

        Apply(Preference, save: false);
    }

    public static void Apply(string preference, bool save = true)
    {
        Preference = preference is "hu" or "en" ? preference : Automatic;
        CurrentLanguage = Preference == Automatic
            ? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "hu" ? "hu" : "en")
            : Preference;

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var old = dictionaries.FirstOrDefault(d =>
            d.Source?.OriginalString.Contains("Localization/Strings.", StringComparison.OrdinalIgnoreCase) == true);
        var replacement = new ResourceDictionary
        {
            Source = new Uri($"/LabelForge.App;component/Localization/Strings.{CurrentLanguage}.xaml", UriKind.Relative)
        };

        if (old is null) dictionaries.Add(replacement);
        else dictionaries[dictionaries.IndexOf(old)] = replacement;

        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(CurrentLanguage);
        if (save) SavePreference();
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void SavePreference()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new LanguageSettings(Preference)));
    }

    private sealed record LanguageSettings(string Language);
}
