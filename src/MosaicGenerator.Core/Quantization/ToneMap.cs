using MosaicGenerator.Core.Colors;

namespace MosaicGenerator.Core.Quantization;

/// <summary>
/// Lays the photograph out in the range the material actually has, before any shade is chosen.
///
/// A mosaicist drawing the cartoon does not copy the photograph's tones. The camera works in a range
/// the glass does not have, so the tones get redistributed into the one it does — that is what the
/// cartoon is for. Skipping that step is what leaves a sky as one flat article: measured on the gull,
/// the matching was accurate (4.3 ΔE) and still put a single shade over 71 % of the panel, because
/// the whole sky sat inside less than one step of the range. Nothing downstream can recover from
/// that: the quantiser is choosing correctly, there is simply nothing left to choose between.
///
/// Two moves, and neither is a setting. Lightness is laid across the range the material has, so a
/// photograph that occupies half of it is opened to all of it. Chroma is folded back inside what the
/// range can reach, hue kept, so a colour the material does not have lands next to its neighbours
/// instead of snapping to whatever happens to be nearest — which is what put hard violet chips in
/// the dolphin's sky.
///
/// It deliberately does no more than that. Equalising the histogram as well — giving a crowded band
/// as much of the range as it has cells — was tried and measured: it bought no gradation the plain
/// stretch had not already bought, and it multiplied the texture inside a flat sky into speckle
/// (banding across neighbouring pieces went from 8 % to 21 %, and the gull's grey wing went black).
/// Spreading is what the material can carry; rearranging is not.
/// </summary>
public static class ToneMap
{
    /// <summary>Hue sectors the chroma ceiling is measured in. Thirty degrees each, interpolated between.</summary>
    private const int HueSectors = 12;

    /// <summary>
    /// Where chroma starts to be folded, as a fraction of what the range reaches. Below the knee a
    /// colour is left exactly as it was: only what the material cannot reach gets moved.
    /// </summary>
    private const double ChromaKnee = 0.8;

    /// <summary>
    /// Redistributes the sampled cells into the palette's own range.
    /// </summary>
    /// <param name="cells">One colour per tessera, as sampled from the photograph.</param>
    /// <param name="palette">The shades as they will be seen — through the joint, if that is on.</param>
    /// <param name="shadeCount">
    /// How many shades the finished work may use. It sets the tonal step of the result, and that step
    /// is the yardstick for how crowded the photograph is: cells closer together than one step of the
    /// finished work cannot be told apart in the material at all.
    /// </param>
    public static CieLab[] IntoPaletteRange(
        ReadOnlySpan<CieLab> cells, ReadOnlySpan<CieLab> palette, int shadeCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(shadeCount, 1);

        if (cells.Length == 0 || palette.Length == 0)
        {
            return cells.ToArray();
        }

        (double paletteLow, double paletteHigh) = LightnessRange(palette);
        (double cellLow, double cellHigh) = LightnessRange(cells);
        double[] ceilings = ChromaCeilings(palette);

        double step = (paletteHigh - paletteLow) / shadeCount;
        double cellSpan = Math.Max(1e-6, cellHigh - cellLow);
        double paletteSpan = paletteHigh - paletteLow;

        // How much tonal separation the photograph actually holds, measured in steps of the finished
        // work. A photograph flatter than one step has nothing to redistribute, and redistributing it
        // anyway would invent a gradient across a wall that is one colour — so at that end the whole
        // mapping fades out and the photograph passes through untouched.
        double reach = Math.Clamp((cellSpan / Math.Max(1e-9, step)) - 1.0, 0.0, 1.0);

        var mapped = new CieLab[cells.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            CieLab cell = cells[i];

            double share = Math.Clamp((cell.L - cellLow) / cellSpan, 0.0, 1.0);
            double target = paletteLow + (share * paletteSpan);
            double l = cell.L + (reach * (target - cell.L));

            (double a, double b) = FoldChroma(cell.A, cell.B, ceilings);
            mapped[i] = new CieLab(l, a, b);
        }

        return mapped;
    }

    /// <summary>
    /// Second and ninety-eighth percentiles rather than the extremes: one stray highlight should not
    /// decide the range the whole picture is laid into.
    /// </summary>
    private static (double Low, double High) LightnessRange(ReadOnlySpan<CieLab> colors)
    {
        double[] sorted = new double[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            sorted[i] = colors[i].L;
        }

        Array.Sort(sorted);
        return (Percentile(sorted, 0.02), Percentile(sorted, 0.98));
    }

    /// <summary>How much chroma the range reaches in each hue sector, as its ninety-fifth percentile.</summary>
    private static double[] ChromaCeilings(ReadOnlySpan<CieLab> palette)
    {
        var bySector = new List<double>[HueSectors];
        for (int s = 0; s < HueSectors; s++)
        {
            bySector[s] = [];
        }

        double most = 0.0;
        foreach (CieLab shade in palette)
        {
            double chroma = Math.Sqrt((shade.A * shade.A) + (shade.B * shade.B));
            most = Math.Max(most, chroma);
            bySector[Sector(shade.A, shade.B)].Add(chroma);
        }

        var ceilings = new double[HueSectors];
        for (int s = 0; s < HueSectors; s++)
        {
            if (bySector[s].Count == 0)
            {
                // A hue the range simply does not carry. Nothing to interpolate from, so let the
                // range as a whole answer rather than folding this hue to nothing.
                ceilings[s] = most;
                continue;
            }

            double[] sorted = [.. bySector[s].Order()];
            ceilings[s] = Math.Max(1e-6, Percentile(sorted, 0.95));
        }

        return ceilings;
    }

    private static int Sector(double a, double b)
    {
        double hue = Math.Atan2(b, a);
        if (hue < 0.0)
        {
            hue += 2.0 * Math.PI;
        }

        return Math.Clamp((int)(hue / (2.0 * Math.PI) * HueSectors), 0, HueSectors - 1);
    }

    /// <summary>
    /// Folds chroma the range cannot reach back inside it, hue untouched. Below the knee nothing
    /// moves; above it the excess is compressed asymptotically, so the order of two saturated colours
    /// survives even though the distance between them does not.
    /// </summary>
    private static (double A, double B) FoldChroma(double a, double b, double[] ceilings)
    {
        double chroma = Math.Sqrt((a * a) + (b * b));
        if (chroma < 1e-6)
        {
            return (a, b);
        }

        double hue = Math.Atan2(b, a);
        if (hue < 0.0)
        {
            hue += 2.0 * Math.PI;
        }

        // Interpolated between sector centres: a hard sector boundary would split a smooth sky in two.
        double position = (hue / (2.0 * Math.PI) * HueSectors) - 0.5;
        int lower = (int)Math.Floor(position);
        double t = position - lower;
        double ceiling = Lerp(
            ceilings[((lower % HueSectors) + HueSectors) % HueSectors],
            ceilings[((lower + 1) % HueSectors + HueSectors) % HueSectors],
            t);

        double knee = ceiling * ChromaKnee;
        if (chroma <= knee)
        {
            return (a, b);
        }

        double headroom = Math.Max(1e-6, ceiling - knee);
        double folded = knee + (headroom * (1.0 - Math.Exp(-(chroma - knee) / headroom)));
        double scale = folded / chroma;
        return (a * scale, b * scale);
    }

    private static double Lerp(double a, double b, double t) => a + (t * (b - a));

    private static double Percentile(double[] sorted, double fraction) =>
        sorted[Math.Clamp((int)Math.Round(fraction * (sorted.Length - 1)), 0, sorted.Length - 1)];
}
