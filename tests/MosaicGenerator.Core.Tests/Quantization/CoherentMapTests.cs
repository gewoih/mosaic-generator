using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Grid;
using MosaicGenerator.Core.Quantization;
using MosaicGenerator.Core.Rendering;

namespace MosaicGenerator.Core.Tests.Quantization;

public class CoherentMapTests
{
    [Fact]
    public void ALoneOutlierSettlesToWhatItsNeighboursChose()
    {
        // A flat field of sky, with one cell nudged just enough by noise that, judged on its own,
        // it is nearer a more saturated article than the sky shade every neighbour sits on.
        CieLab sky = new(70, 0, -20);
        CieLab saturated = new(72, 0, -8);
        CieLab outlier = new(71.5, 0, -13.5);

        CieLab[] cellLab = [sky, sky, outlier, sky, sky];
        CieLab[] paletteLab = [sky, saturated];
        CellNeighbourhood neighbourhood = CellNeighbourhood.Build(LineOf(5, spacing: 10), reach: 11);

        int[] initial = Quantizer.Map(cellLab, paletteLab);
        Assert.Equal(1, initial[2]); // judged alone, the outlier lands on the saturated article

        int[] settled = CoherentMap.Settle(cellLab, paletteLab, initial, [0, 1], neighbourhood);

        Assert.Equal(0, settled[2]);
        Assert.Equal(initial[0], settled[0]);
        Assert.Equal(initial[4], settled[4]);
    }

    [Fact]
    public void ARealBoundaryStaysExactlyWhereItWas()
    {
        // Two halves twenty ΔE apart — a beak against sky, in miniature. Far past the falloff,
        // so the two sides should carry essentially no weight for each other.
        CieLab dark = new(50, 0, 0);
        CieLab light = new(70, 0, 0);
        CieLab[] palette = [dark, light];

        CieLab[] cellLab = [.. Enumerable.Range(0, 20).Select(i => i < 10 ? dark : light)];
        CellNeighbourhood neighbourhood = CellNeighbourhood.Build(LineOf(20, spacing: 10), reach: 11);

        int[] initial = Quantizer.Map(cellLab, palette);
        int[] settled = CoherentMap.Settle(cellLab, palette, initial, [0, 1], neighbourhood);

        Assert.Equal(initial, settled);
    }

    [Fact]
    public void AGradientKeepsEveryShadeItStartedWith()
    {
        // A smooth ramp across five shades. Settling should move the boundary between shades, not
        // erase a shade — that would be the sky-collapses-to-one-article failure the plan warns about.
        CieLab[] palette = [.. Enumerable.Range(0, 5).Select(i => new CieLab(i * 25.0, 0, 0))];
        CieLab[] cellLab = [.. Enumerable.Range(0, 21).Select(i => new CieLab(i * 100.0 / 20, 0, 0))];
        CellNeighbourhood neighbourhood = CellNeighbourhood.Build(LineOf(21, spacing: 10), reach: 11);

        int[] initial = Quantizer.Map(cellLab, palette);
        int[] settled = CoherentMap.Settle(cellLab, palette, initial, [0, 1, 2, 3, 4], neighbourhood);

        Assert.Equal(initial.Distinct().Count(), settled.Distinct().Count());
    }

    [Fact]
    public void WithNoNeighboursTheResultMatchesThePlainQuantizer()
    {
        CieLab[] palette = [new(30, 10, 10), new(80, -10, -10)];
        CieLab[] cellLab = [.. Enumerable.Range(0, 6).Select(i => new CieLab(i * 15.0, i % 2 == 0 ? 5 : -5, 0))];

        // Reach shorter than the spacing: nobody is within range of anybody.
        CellNeighbourhood neighbourhood = CellNeighbourhood.Build(LineOf(6, spacing: 100), reach: 1);

        int[] initial = Quantizer.Map(cellLab, palette);
        int[] settled = CoherentMap.Settle(cellLab, palette, initial, [0, 1], neighbourhood);

        Assert.Equal(initial, settled);
    }

    [Fact]
    public void ALoudLoneSingleSnapsToTheArticleAroundIt()
    {
        // 5×5 field of one shade; the middle cell carries enough colour that, judged alone, it
        // quantises onto a saturated article far from the field — a loud single no neighbour shares.
        CieLab sky = new(60, 2, -15);
        CieLab loud = new(62, 22, -32);
        CieLab[] palette = [sky, loud];

        CieLab[] cellLab = [.. Enumerable.Repeat(sky, 25)];
        cellLab[12] = new CieLab(61, 18, -29); // nearest to `loud`

        CellNeighbourhood hood = CellNeighbourhood.Build(GridOf(5, 5, spacing: 10), reach: 15);
        int[] initial = Quantizer.Map(cellLab, palette);
        Assert.Equal(1, initial[12]);

        int[] settled = CoherentMap.Settle(cellLab, palette, initial, [0, 1], hood);

        Assert.Equal(0, settled[12]);
        Assert.Equal(0, settled[0]);
        Assert.Equal(0, settled[24]);
    }

    [Fact]
    public void ALoudLoneSingleSnapsEvenWhenTheRingCarriesTwoArticles()
    {
        // The ring around the middle cell is split 4/4 between two field shades — not unanimous,
        // but they agree that the middle cell's loud third article does not belong. It snaps to the
        // article most of the ring carries (ties to the lower index).
        CieLab left = new(45, 0, 2);
        CieLab right = new(52, 0, -2);
        CieLab loud = new(60, 25, 20);
        CieLab[] palette = [left, right, loud];

        var cellLab = new CieLab[25];
        for (int r = 0; r < 5; r++)
        {
            for (int c = 0; c < 5; c++)
            {
                cellLab[(r * 5) + c] = c < 2 ? left : right;
            }
        }

        cellLab[12] = loud;

        CellNeighbourhood hood = CellNeighbourhood.Build(GridOf(5, 5, spacing: 10), reach: 15);
        int[] initial = Quantizer.Map(cellLab, palette);
        Assert.Equal(2, initial[12]);

        int[] settled = CoherentMap.Settle(cellLab, palette, initial, [0, 1, 2], hood);

        Assert.NotEqual(2, settled[12]);
    }

    [Fact]
    public void APieceThatSharesAnArticleWithAnyNeighbourIsLeftAlone()
    {
        // The middle column is a deliberate line of a second shade. Every one of its cells has a
        // same-article neighbour along the line, so none is a lone single — the strip stays whole,
        // and so would a strip end, a one-wide line, or any island of two.
        CieLab field = new(55, 0, -5);
        CieLab line = new(58, 20, -28);
        CieLab[] palette = [field, line];

        var cellLab = new CieLab[25];
        for (int r = 0; r < 5; r++)
        {
            for (int c = 0; c < 5; c++)
            {
                cellLab[(r * 5) + c] = c == 2 ? line : field;
            }
        }

        CellNeighbourhood hood = CellNeighbourhood.Build(GridOf(5, 5, spacing: 10), reach: 15);
        int[] initial = Quantizer.Map(cellLab, palette);
        int[] settled = CoherentMap.Settle(cellLab, palette, initial, [0, 1], hood);

        for (int r = 0; r < 5; r++)
        {
            Assert.Equal(1, settled[(r * 5) + 2]);
        }
    }

    [Fact]
    public void ANearbyShadeIsNotSnappedAwayJustForBeingAlone()
    {
        // The middle cell is a lone single sitting exactly on a shade only ~4 ΔE from the field's —
        // below the loud gap, so the isolation pull does not fire and the ordinary term keeps the
        // cell where its own colour puts it.
        CieLab field = new(60, 0, -10);
        CieLab close = new(61, 0, -6);
        CieLab[] palette = [field, close];

        CieLab[] cellLab = [.. Enumerable.Repeat(field, 25)];
        cellLab[12] = close;

        CellNeighbourhood hood = CellNeighbourhood.Build(GridOf(5, 5, spacing: 10), reach: 15);
        int[] initial = Quantizer.Map(cellLab, palette);
        Assert.Equal(1, initial[12]);

        int[] settled = CoherentMap.Settle(cellLab, palette, initial, [0, 1], hood);

        Assert.Equal(1, settled[12]);
    }

    private static Tessera[] GridOf(int cols, int rows, double spacing) =>
        [.. Enumerable.Range(0, cols * rows).Select(k =>
        {
            int cx = k % cols, cy = k / cols;
            double x = cx * spacing, y = cy * spacing;
            return new Tessera
            {
                Polygon = [new(x, y), new(x + 8, y), new(x + 8, y + 8), new(x, y + 8)],
                Centroid = new PointD(x, y),
                AreaMm2 = 64,
                CourseId = cy,
                IndexInCourse = cx,
                IsCut = false,
            };
        })];

    private static Tessera[] LineOf(int count, double spacing) =>
        [.. Enumerable.Range(0, count).Select(i => new Tessera
        {
            Polygon = [new(i * spacing, 0), new((i * spacing) + 8, 0), new((i * spacing) + 8, 8), new(i * spacing, 8)],
            Centroid = new PointD(i * spacing, 4),
            AreaMm2 = 64,
            CourseId = 0,
            IndexInCourse = i,
            IsCut = false,
        })];
}
