using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Quantization;

namespace MosaicGenerator.Core.Tests.Quantization;

public class ToneMapTests
{
    /// <summary>A range spanning L* 30 to 95, chroma up to about 40 — roughly a smalt catalogue.</summary>
    private static CieLab[] Range()
    {
        var shades = new List<CieLab>();
        for (int l = 30; l <= 95; l += 5)
        {
            for (int hue = 0; hue < 360; hue += 45)
            {
                double radians = hue * Math.PI / 180.0;
                shades.Add(new CieLab(l, 40.0 * Math.Cos(radians), 40.0 * Math.Sin(radians)));
            }
        }

        return [.. shades];
    }

    [Fact]
    public void AFlatPhotographIsLeftExactlyAsItIs()
    {
        // A wall of one colour must stay a wall of one colour. Spreading it by rank would invent a
        // gradient the photograph does not have — this is the failure the pipeline test caught.
        CieLab[] cells = [.. Enumerable.Repeat(new CieLab(60, 5, -10), 400)];

        CieLab[] mapped = ToneMap.IntoPaletteRange(cells, Range(), shadeCount: 12);

        foreach (CieLab cell in mapped)
        {
            Assert.Equal(60.0, cell.L, 3);
            Assert.Equal(5.0, cell.A, 3);
            Assert.Equal(-10.0, cell.B, 3);
        }
    }

    [Fact]
    public void APhotographUsingHalfTheRangeIsOpenedToAllOfIt()
    {
        // The gull's disease: the camera works in a range the glass does not have, so the picture
        // arrives occupying part of what the material could say. Everything downstream is then
        // choosing between fewer articles than exist.
        var random = new Random(20260902);
        CieLab[] cells = [.. Enumerable.Range(0, 800)
            .Select(_ => new CieLab(55.0 + (random.NextDouble() * 25.0), -2, -14))];

        CieLab[] mapped = ToneMap.IntoPaletteRange(cells, Range(), shadeCount: 12);

        double before = mapped.Length == 0 ? 0 : cells.Max(c => c.L) - cells.Min(c => c.L);
        double after = mapped.Max(c => c.L) - mapped.Min(c => c.L);

        Assert.True(after > before * 2.0, $"span {before:0.0} -> {after:0.0} L*");
        Assert.InRange(mapped.Min(c => c.L), 28.0, 36.0);
        Assert.InRange(mapped.Max(c => c.L), 89.0, 97.0);
    }

    [Fact]
    public void ACrowdedBandIsNotGivenMoreOfTheRangeThanItsShare()
    {
        // Equalising the histogram was tried and measured: it bought no gradation the plain stretch
        // had not already bought, and multiplied the texture of a flat sky into speckle. Two thirds
        // of the panel sitting inside two L* must stay narrow relative to the subject around it.
        var cells = new List<CieLab>();
        for (int i = 0; i < 700; i++)
        {
            cells.Add(new CieLab(78.0 + (i % 20 * 0.1), -2, -14));
        }

        for (int i = 0; i < 300; i++)
        {
            cells.Add(new CieLab(35.0 + (i % 40), 3, 6));
        }

        CieLab[] mapped = ToneMap.IntoPaletteRange([.. cells], Range(), shadeCount: 12);

        double sky = mapped.Take(700).Max(c => c.L) - mapped.Take(700).Min(c => c.L);
        double subject = mapped.Skip(700).Max(c => c.L) - mapped.Skip(700).Min(c => c.L);

        Assert.True(sky < subject * 0.2, $"sky took {sky:0.0} L* against the subject's {subject:0.0}");
    }

    [Fact]
    public void TheOrderOfTonesIsNeverReversed()
    {
        // Whatever it does to the spacing, it may not make a lighter cell darker than a darker one:
        // that would rearrange the picture rather than lay it out.
        var random = new Random(20260902);
        CieLab[] cells = [.. Enumerable.Range(0, 500)
            .Select(_ => new CieLab(random.NextDouble() * 100.0, random.NextDouble() * 30.0 - 15.0, 8))];

        CieLab[] mapped = ToneMap.IntoPaletteRange(cells, Range(), shadeCount: 10);

        for (int i = 0; i < cells.Length; i++)
        {
            for (int j = i + 1; j < cells.Length; j++)
            {
                if (cells[i].L < cells[j].L - 1e-9)
                {
                    Assert.True(
                        mapped[i].L <= mapped[j].L + 1e-6,
                        $"{cells[i].L:0.00} < {cells[j].L:0.00} became {mapped[i].L:0.00} > {mapped[j].L:0.00}");
                }
            }
        }
    }

    [Fact]
    public void ChromaTheRangeCannotReachIsFoldedBackInsideIt()
    {
        // The dolphin's disease: a sky more saturated than any article, where matching each cell on
        // its own picks wildly different shades for colours the photograph barely separates.
        CieLab[] cells = [.. Enumerable.Range(0, 200).Select(i => new CieLab(55, 0, -70.0 - (i * 0.1)))];

        CieLab[] mapped = ToneMap.IntoPaletteRange(cells, Range(), shadeCount: 12);

        foreach (CieLab cell in mapped)
        {
            Assert.True(Chroma(cell) < 45.0, $"chroma {Chroma(cell):0.0} still outside the range");
        }

        // Hue survives the fold: a blue sky must not come back green.
        Assert.All(mapped, cell => Assert.True(cell.B < 0 && Math.Abs(cell.A) < 5.0));
    }

    [Fact]
    public void ChromaTheRangeAlreadyReachesIsLeftAlone()
    {
        CieLab[] cells = [.. Enumerable.Range(0, 200).Select(i => new CieLab(55, 10, -12.0 - (i * 0.01)))];

        CieLab[] mapped = ToneMap.IntoPaletteRange(cells, Range(), shadeCount: 12);

        for (int i = 0; i < cells.Length; i++)
        {
            Assert.Equal(cells[i].A, mapped[i].A, 6);
            Assert.Equal(cells[i].B, mapped[i].B, 6);
        }
    }

    [Fact]
    public void AnEmptyLayoutIsHandedBackUnchanged()
    {
        Assert.Empty(ToneMap.IntoPaletteRange([], Range(), shadeCount: 12));
    }

    private static double Chroma(CieLab lab) => Math.Sqrt((lab.A * lab.A) + (lab.B * lab.B));

}
