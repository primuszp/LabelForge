namespace LabelForge.Core;

public enum AttributeType
{
    SingleSelect,
    FreeText
}

public sealed record AttributeOption(string Value, string DisplayName);

public sealed record AttributeDefinition(
    string Key,
    string DisplayName,
    AttributeType Type,
    IReadOnlyList<AttributeOption> Options);

public static class DefaultAttributeSchema
{
    public static readonly IReadOnlyList<AttributeDefinition> Definitions =
    [
        new("direction", "Irány", AttributeType.SingleSelect,
        [
            new("entering", "Belép"),
            new("leaving", "Kilép"),
            new("passing", "Áthalad"),
            new("unknown", "Ismeretlen")
        ]),
        new("subject_type", "Alany", AttributeType.SingleSelect,
        [
            new("person", "Személy"),
            new("vehicle", "Jármű"),
            new("animal", "Állat"),
            new("other", "Egyéb")
        ]),
        new("quality", "Minőség", AttributeType.SingleSelect,
        [
            new("good", "Jó"),
            new("blurry", "Homályos"),
            new("dark", "Sötét"),
            new("overexposed", "Túlexponált")
        ]),
        new("season", "Évszak", AttributeType.SingleSelect,
        [
            new("spring", "Tavasz"),
            new("summer", "Nyár"),
            new("autumn", "Ősz"),
            new("winter", "Tél")
        ]),
        new("daytime", "Napszak", AttributeType.SingleSelect,
        [
            new("day", "Nappal"),
            new("night", "Éjszaka"),
            new("dusk", "Szürkület")
        ]),
        new("weather", "Időjárás", AttributeType.SingleSelect,
        [
            new("sunny", "Napos"),
            new("cloudy", "Felhős"),
            new("rainy", "Esős"),
            new("foggy", "Ködös"),
            new("snowy", "Havas")
        ]),
    ];
}
