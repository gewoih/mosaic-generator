using System.Globalization;
using MosaicGenerator.Core.Domain;

namespace MosaicGenerator.Core.Material;

/// <summary>
/// Smalt arrives as pieces of varying size, so there is no plate format to lay out against.
/// Consumption is therefore accounted by area: module area, scaled by the waste allowance,
/// converted to mass through thickness and density, and priced at one average rate per kilogram.
/// </summary>
public static class MaterialCalculator
{
    public static MaterialReport Calculate(MosaicPlan plan, double wasteFactor, decimal pricePerKg)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentOutOfRangeException.ThrowIfLessThan(wasteFactor, 1.0);
        ArgumentOutOfRangeException.ThrowIfNegative(pricePerKg);

        var counts = new Dictionary<int, int>();
        var areaMm2 = new Dictionary<int, double>();
        for (int i = 0; i < plan.ColorIndices.Length; i++)
        {
            int index = plan.ColorIndices[i];
            counts[index] = counts.GetValueOrDefault(index) + 1;

            // The tesserae are not all a full module: courses that follow the form get cut where
            // they meet the field edge, so consumption is summed from the actual outlines.
            areaMm2[index] = areaMm2.GetValueOrDefault(index) + plan.Tesserae[i].AreaMm2;
        }

        var ordered = counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => plan.Palette.Colors[pair.Key].Article, StringComparer.Ordinal)
            .ToList();

        var lines = new List<MaterialLine>(ordered.Count);
        var indexByPalette = new Dictionary<int, int>(ordered.Count);

        for (int position = 0; position < ordered.Count; position++)
        {
            (int paletteIndex, int moduleCount) = ordered[position];
            PaletteColor color = plan.Palette.Colors[paletteIndex];

            double netAreaM2 = areaMm2[paletteIndex] / 1_000_000.0;
            double grossAreaM2 = netAreaM2 * wasteFactor;
            double massKg = grossAreaM2 * (color.ThicknessMm / 1000.0) * color.DensityKgPerM3;

            lines.Add(new MaterialLine
            {
                Code = (position + 1).ToString(CultureInfo.InvariantCulture),
                Color = color,
                ModuleCount = moduleCount,
                NetAreaM2 = netAreaM2,
                GrossAreaM2 = grossAreaM2,
                MassKg = massKg,
                Cost = (decimal)massKg * pricePerKg,
            });

            indexByPalette[paletteIndex] = position;
        }

        return new MaterialReport
        {
            Lines = lines,
            WasteFactor = wasteFactor,
            PricePerKg = pricePerKg,
            IndexByPalette = indexByPalette,
        };
    }
}
