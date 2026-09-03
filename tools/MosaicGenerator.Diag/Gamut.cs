using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Imaging;

namespace MosaicGenerator.Diag;

/// <summary>
/// How far the photograph sits from anything the range can actually supply, before any layout or
/// colour ceiling is involved. A picture whose colours are simply not in the box cannot be rescued
/// by finer tesserae or by more articles — only by moving the picture into the box.
/// </summary>
internal static class Gamut
{
    public static void Report(SourceImage image, Palette palette, int cols = 44)
    {
        CieLab[] shades = [.. palette.Colors.Select(c => c.Lab)];

        int rows = Math.Max(1, (int)Math.Round(cols * (double)image.Height / image.Width / 2.0));
        int blockW = Math.Max(1, image.Width / cols);
        int blockH = Math.Max(1, image.Height / rows);

        var distances = new List<double>(cols * rows);
        var map = new List<string>(rows);

        for (int r = 0; r < rows; r++)
        {
            var line = new System.Text.StringBuilder(cols);
            for (int c = 0; c < cols; c++)
            {
                CieLab lab = image
                    .AverageLinear(c * blockW, r * blockH, blockW, blockH)
                    .ToLab();

                double best = double.MaxValue;
                foreach (CieLab shade in shades)
                {
                    best = Math.Min(best, ColorDistance.CieDe76Squared(lab, shade));
                }

                best = Math.Sqrt(best);
                distances.Add(best);
                line.Append(" .:-=+*#%@"[Math.Min(9, (int)(best / 2.5))]);
            }

            map.Add(line.ToString());
        }

        distances.Sort();
        Console.WriteLine("\nДо ближайшей смальты (ΔE76, по блокам фотографии):");
        Console.WriteLine(
            $"  медиана {distances[distances.Count / 2]:0.0}   "
            + $"p90 {distances[(int)(distances.Count * 0.9)]:0.0}   "
            + $"максимум {distances[^1]:0.0}   "
            + $"блоков дальше 10 ΔE: {distances.Count(d => d > 10) * 100.0 / distances.Count:0}%");
        Console.WriteLine("  карта — пробел ближе всего, @ дальше всего (шаг 2,5 ΔE):");
        foreach (string line in map)
        {
            Console.WriteLine("  |" + line + "|");
        }

        Console.WriteLine();
    }

    /// <summary>
    /// How much the picture separates one band from the next, down its middle. A mosaic in
    /// <paramref name="shadeCount"/> shades has a tonal step of the range divided by that many, and
    /// anything the photograph separates by less than a step arrives as one colour however many
    /// pieces are laid — which is how a hazy mountain ridge comes out the same blue as the sky
    /// behind it.
    /// </summary>
    public static void Contrast(SourceImage image, Palette palette, int shadeCount, int rows = 30)
    {
        CieLab[] shades = [.. palette.Colors.Select(c => c.Lab)];
        double[] lightness = [.. shades.Select(s => s.L).Order()];
        double step = (lightness[^3] - lightness[2]) / shadeCount;

        int blockH = Math.Max(1, image.Height / rows);
        int x0 = image.Width / 4;
        int blockW = Math.Max(1, image.Width / 2);

        Console.WriteLine(
            $"Профиль по средней полосе, шаг тона готовой работы {step:0.0} ΔE ({shadeCount} оттенков):");

        CieLab? previous = null;
        for (int r = 0; r < rows; r++)
        {
            CieLab band = image.AverageLinear(x0, r * blockH, blockW, blockH).ToLab();
            double delta = previous is null ? 0.0 : ColorDistance.CieDe76(band, previous.Value);
            previous = band;

            string flag = r == 0 ? "" : delta < step ? "  ← меньше шага" : string.Empty;
            Console.WriteLine(
                $"  y={(double)r / rows:0.00}  L*={band.L,5:0.0}  до предыдущей полосы {delta,5:0.0} ΔE{flag}");
        }

        Console.WriteLine();
    }
}
