using MosaicGenerator.Core.Domain;

namespace MosaicGenerator.Core.Tests.Support;

internal static class RequestFactory
{
    public static MosaicRequest Request(
        double panelWidth = 1000,
        double panelHeight = 1000,
        double module = 20,
        double grout = 3,
        double wastePercent = 25,
        string paletteId = "test",
        decimal pricePerKg = 3200m,
        int maxColors = 100,
        ulong seed = 0) => new()
        {
            PanelWidthMm = panelWidth,
            PanelHeightMm = panelHeight,
            ModuleWidthMm = module,
            ModuleHeightMm = module,
            GroutWidthMm = grout,
            WastePercent = wastePercent,
            PaletteId = paletteId,
            PricePerKgRub = pricePerKg,
            MaxColors = maxColors,
            Seed = seed,
        };

    public static MosaicLayout Layout(
        double panelWidth = 1000,
        double panelHeight = 1000,
        double module = 20,
        double grout = 3) =>
        MosaicLayout.Compute(Request(panelWidth, panelHeight, module, grout));
}
