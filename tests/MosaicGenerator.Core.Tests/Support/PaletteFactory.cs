using MosaicGenerator.Core.Domain;

namespace MosaicGenerator.Core.Tests.Support;

internal static class PaletteFactory
{
    public static PaletteColor Color(
        string hex,
        string article = "SM-001",
        string name = "Тест",
        double thicknessMm = 8,
        double densityKgPerM3 = 2500) =>
        new(article, name, hex, thicknessMm, densityKgPerM3);

    public static Palette Of(params PaletteColor[] colors) => new("test", "Тестовая", colors);

    public static Palette OfHex(params string[] hexes) =>
        Of([.. hexes.Select((hex, i) => Color(hex, article: $"SM-{i + 1:000}"))]);

    /// <summary>Layout shorthand for tests that only care about the module and the joint.</summary>
    public static MosaicLayout Layout(double module, double grout) =>
        RequestFactory.Layout(module: module, grout: grout);
}
