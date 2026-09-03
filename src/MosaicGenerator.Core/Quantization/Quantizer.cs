using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;

namespace MosaicGenerator.Core.Quantization;

public static class Quantizer
{
    /// <summary>
    /// Maps each cell to its nearest palette entry in CIELAB, matching against the shades
    /// themselves. Nearest-in-RGB would follow the encoding rather than perception and pick
    /// visibly wrong shades. No dithering: scattering stray tesserae through a field reads as
    /// noise in a mosaic, not as a blend.
    /// </summary>
    public static int[] Map(ReadOnlySpan<LinearRgb> cells, Palette palette) =>
        Map(ToLab(cells), PaletteObservation.Lab(palette));

    /// <summary>
    /// Maps each cell to its nearest entry of a prepared palette table. Both sides arrive already
    /// in CIELAB so the caller decides what the palette stands for — the bare shades, or the
    /// shades as the joint around them will show them.
    /// </summary>
    public static int[] Map(ReadOnlySpan<CieLab> cellLab, ReadOnlySpan<CieLab> paletteLab)
    {
        if (paletteLab.Length == 0)
        {
            throw new ArgumentException("A palette needs at least one colour.", nameof(paletteLab));
        }

        var indices = new int[cellLab.Length];
        for (int i = 0; i < cellLab.Length; i++)
        {
            indices[i] = NearestIndex(cellLab[i], paletteLab);
        }

        return indices;
    }

    /// <summary>
    /// Converts cells once and keeps them. Every cell is measured against the palette repeatedly —
    /// once to map, then once per round of the colour reduction — and the conversion is the
    /// expensive half of that.
    /// </summary>
    public static CieLab[] ToLab(ReadOnlySpan<LinearRgb> cells)
    {
        var lab = new CieLab[cells.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            lab[i] = cells[i].ToLab();
        }

        return lab;
    }

    public static int NearestIndex(CieLab target, ReadOnlySpan<CieLab> candidates)
    {
        int best = 0;
        double bestDistance = double.MaxValue;

        for (int i = 0; i < candidates.Length; i++)
        {
            double distance = ColorDistance.Match(target, candidates[i]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }
}
