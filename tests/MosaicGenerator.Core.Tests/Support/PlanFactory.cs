using MosaicGenerator.Core.Domain;

namespace MosaicGenerator.Core.Tests.Support;

internal static class PlanFactory
{
    /// <summary>A plan whose cells cycle through the palette, so every colour is exercised.</summary>
    public static MosaicPlan Striped(
        ulong seed,
        double panelWidth = 300,
        double panelHeight = 200,
        double module = 20,
        double grout = 3,
        string[]? hexes = null)
    {
        Palette palette = PaletteFactory.OfHex(hexes ?? ["#F2EFE6", "#3C6E71", "#B33951", "#1E2019"]);
        MosaicLayout layout = RequestFactory.Layout(panelWidth, panelHeight, module, grout);

        var indices = new int[layout.TotalModules];
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = i % palette.Colors.Count;
        }

        return new MosaicPlan(layout, palette, indices, seed);
    }
}
