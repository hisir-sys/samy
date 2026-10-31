using System.Globalization;

namespace Samy.Core;

/// <summary>One unit inside a category. <see cref="ToBase"/> multiplies a value into the category's base unit.</summary>
public sealed record UnitDef(string Symbol, string Name, double ToBase);

public sealed record UnitCategory(string Name, IReadOnlyList<UnitDef> Units)
{
    public UnitDef? Find(string symbol) =>
        Units.FirstOrDefault(u => string.Equals(u.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Pure unit conversion logic. Temperature is handled specially because it is not a pure scale.</summary>
public static class UnitConverter
{
    public const string TemperatureName = "Temperature";

    public static IReadOnlyList<UnitCategory> Categories { get; } = new List<UnitCategory>
    {
        new("Length", new UnitDef[]
        {
            new("mm", "Millimetres", 0.001), new("cm", "Centimetres", 0.01), new("m", "Metres", 1),
            new("km", "Kilometres", 1000), new("in", "Inches", 0.0254), new("ft", "Feet", 0.3048),
            new("yd", "Yards", 0.9144), new("mi", "Miles", 1609.344),
        }),
        new("Weight", new UnitDef[]
        {
            new("g", "Grams", 0.001), new("kg", "Kilograms", 1), new("t", "Tonnes", 1000),
            new("oz", "Ounces", 0.028349523125), new("lb", "Pounds", 0.45359237),
        }),
        new(TemperatureName, new UnitDef[]
        {
            new("°C", "Celsius", 1), new("°F", "Fahrenheit", 1), new("K", "Kelvin", 1),
        }),
        new("Data", new UnitDef[]
        {
            // Binary (1024) steps, matching how SAMY already treated MB -> GB.
            new("B", "Bytes", 1), new("KB", "Kilobytes", 1024), new("MB", "Megabytes", 1024.0 * 1024),
            new("GB", "Gigabytes", 1024.0 * 1024 * 1024), new("TB", "Terabytes", 1024.0 * 1024 * 1024 * 1024),
        }),
        new("Volume", new UnitDef[]
        {
            new("mL", "Millilitres", 0.001), new("L", "Litres", 1), new("tsp", "Teaspoons (US)", 0.00492892159375),
            new("tbsp", "Tablespoons (US)", 0.01478676478125), new("fl oz", "Fluid ounces (US)", 0.0295735295625),
            new("cup", "Cups (US)", 0.2365882365), new("gal", "Gallons (US)", 3.785411784),
        }),
        new("Speed", new UnitDef[]
        {
            new("m/s", "Metres per second", 1), new("km/h", "Kilometres per hour", 1000.0 / 3600),
            new("mph", "Miles per hour", 0.44704), new("kn", "Knots", 1852.0 / 3600),
        }),
        new("Time", new UnitDef[]
        {
            new("s", "Seconds", 1), new("min", "Minutes", 60), new("h", "Hours", 3600),
            new("day", "Days", 86400), new("wk", "Weeks", 604800),
        }),
    };

    public static UnitCategory? FindCategory(string name) =>
        Categories.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Converts <paramref name="value"/>. Returns false when the category or a unit is unknown, or the result is not finite.</summary>
    public static bool TryConvert(string category, string fromSymbol, string toSymbol, double value, out double result)
    {
        result = double.NaN;
        var cat = FindCategory(category);
        var from = cat?.Find(fromSymbol);
        var to = cat?.Find(toSymbol);
        if (cat is null || from is null || to is null || !double.IsFinite(value)) return false;

        result = string.Equals(cat.Name, TemperatureName, StringComparison.OrdinalIgnoreCase)
            ? FromCelsius(ToCelsius(value, from.Symbol), to.Symbol)
            : value * from.ToBase / to.ToBase;
        return double.IsFinite(result);
    }

    /// <summary>Parses user text using the invariant culture, also accepting a comma as the decimal separator.</summary>
    public static bool TryParseNumber(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string t = text.Trim();
        // "1,5" -> 1.5 when there is exactly one comma and no dot. Anything else uses invariant rules.
        if (t.Count(c => c == ',') == 1 && !t.Contains('.')) t = t.Replace(',', '.');
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    public static string Format(double value) =>
        Math.Round(value, 6, MidpointRounding.AwayFromZero).ToString("0.######", CultureInfo.InvariantCulture);

    private static double ToCelsius(double v, string symbol) => symbol switch
    {
        "°F" => (v - 32) * 5 / 9,
        "K" => v - 273.15,
        _ => v,
    };

    private static double FromCelsius(double c, string symbol) => symbol switch
    {
        "°F" => c * 9 / 5 + 32,
        "K" => c + 273.15,
        _ => c,
    };
}
