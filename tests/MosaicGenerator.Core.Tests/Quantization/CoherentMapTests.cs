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
