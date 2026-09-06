using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Grid;
using MosaicGenerator.Core.Quantization;
using MosaicGenerator.Core.Rendering;
using MosaicGenerator.Core.Tests.Support;

namespace MosaicGenerator.Core.Tests.Quantization;

public class PaletteReducerTests
{
    [Fact]
    public void ReducesToExactlyTheRequestedNumberOfColours()
    {
        Palette palette = PaletteFactory.OfHex("#FFFFFF", "#C0C0C0", "#808080", "#404040", "#000000");
        LinearRgb[] cells = Cells(palette, [40, 30, 20, 6, 2]);
        int[] indices = Quantizer.Map(cells, palette);

        ReductionOutcome outcome = PaletteReducer.Reduce(cells, indices, palette, maxColors: 3);

        Assert.Equal(5, outcome.ColorsBefore);
        Assert.Equal(3, outcome.ColorsAfter);
        Assert.Equal(3, outcome.Indices.Distinct().Count());
    }

    [Fact]
    public void EveryModuleSurvivesTheReduction()
    {
        Palette palette = PaletteFactory.OfHex("#FFFFFF", "#C0C0C0", "#808080", "#404040", "#000000");
        LinearRgb[] cells = Cells(palette, [40, 30, 20, 6, 2]);
        int[] indices = Quantizer.Map(cells, palette);

        ReductionOutcome outcome = PaletteReducer.Reduce(cells, indices, palette, maxColors: 3);

        Assert.Equal(cells.Length, outcome.Indices.Length);
        Assert.Equal(8, outcome.ModulesReassigned);
    }

    [Fact]
    public void ADiscardedCellGoesToTheNearestSurvivorNotTheFirstCandidate()
    {
        // The rare shade sits next to white, but white is the last palette entry. Falling back to
        // the first candidate would put it on black.
        Palette palette = PaletteFactory.OfHex("#000000", "#EEEEEE", "#FFFFFF");
        LinearRgb[] cells =
        [
            .. Repeat("#000000", 10),
            Rgb.FromHex("#EEEEEE").ToLinear(),
            .. Repeat("#FFFFFF", 10),
        ];
        int[] indices = Quantizer.Map(cells, palette);
        Assert.Equal(1, indices[10]);

        ReductionOutcome outcome = PaletteReducer.Reduce(cells, indices, palette, maxColors: 2);

        Assert.Equal(2, outcome.Indices[10]);
        Assert.Equal(1, outcome.ModulesReassigned);
    }

    [Fact]
    public void CellsOfOneDiscardedShadeCanLandOnDifferentSurvivors()
    {
        // Both cells quantise to the mid grey, but one is closer to black and the other to white.
        // Moving the whole shade to a single replacement would misplace one of them.
        Palette palette = PaletteFactory.OfHex("#000000", "#808080", "#FFFFFF");
        LinearRgb[] cells =
        [
            .. Repeat("#000000", 5),
            Rgb.FromHex("#6A6A6A").ToLinear(),
            Rgb.FromHex("#969696").ToLinear(),
            .. Repeat("#FFFFFF", 5),
        ];
        int[] indices = Quantizer.Map(cells, palette);
        Assert.Equal(1, indices[5]);
        Assert.Equal(1, indices[6]);

        ReductionOutcome outcome = PaletteReducer.Reduce(cells, indices, palette, maxColors: 2);

        Assert.Equal(0, outcome.Indices[5]);
        Assert.Equal(2, outcome.Indices[6]);
    }

    [Fact]
    public void ACapAboveTheColoursInUseChangesNothing()
    {
        Palette palette = PaletteFactory.OfHex("#FFFFFF", "#808080", "#000000");
        LinearRgb[] cells = Cells(palette, [5, 5, 5]);
        int[] indices = Quantizer.Map(cells, palette);

        ReductionOutcome outcome = PaletteReducer.Reduce(cells, indices, palette, maxColors: 50);

        Assert.Same(indices, outcome.Indices);
        Assert.Equal(0, outcome.ModulesReassigned);
        Assert.Equal(outcome.ColorsBefore, outcome.ColorsAfter);
    }

    [Fact]
    public void ACapOfOneCollapsesTheWholeLayoutToASingleShade()
    {
        Palette palette = PaletteFactory.OfHex("#FFFFFF", "#808080", "#000000");
        LinearRgb[] cells = Cells(palette, [10, 3, 1]);
        int[] indices = Quantizer.Map(cells, palette);

        ReductionOutcome outcome = PaletteReducer.Reduce(cells, indices, palette, maxColors: 1);

        Assert.Equal(1, outcome.ColorsAfter);
        Assert.Single(outcome.Indices.Distinct());
        Assert.Equal(4, outcome.ModulesReassigned);
    }

    [Fact]
    public void TiedCountsResolveTheSameWayEveryRun()
    {
        Palette palette = PaletteFactory.OfHex("#FFFFFF", "#C0C0C0", "#808080", "#000000");
        LinearRgb[] cells = Cells(palette, [10, 3, 3, 10]);
        int[] first = Quantizer.Map(cells, palette);
        int[] second = Quantizer.Map(cells, palette);

        ReductionOutcome a = PaletteReducer.Reduce(cells, first, palette, maxColors: 3);
        ReductionOutcome b = PaletteReducer.Reduce(cells, second, palette, maxColors: 3);

        Assert.Equal(a.Indices, b.Indices);
    }

    [Fact]
    public void TheOriginalMappingIsLeftIntact()
    {
        Palette palette = PaletteFactory.OfHex("#FFFFFF", "#808080", "#000000");
        LinearRgb[] cells = Cells(palette, [10, 1, 10]);
        int[] indices = Quantizer.Map(cells, palette);
        int[] before = [.. indices];

        PaletteReducer.Reduce(cells, indices, palette, maxColors: 2);

        Assert.Equal(before, indices);
    }

    [Fact]
    public void AMismatchedCellCountIsRejected()
    {
        Palette palette = PaletteFactory.OfHex("#FFFFFF", "#000000");
        LinearRgb[] cells = Cells(palette, [2, 2]);

        Assert.Throws<ArgumentException>(() => PaletteReducer.Reduce(cells, [0, 1], palette, 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PaletteReducer.Reduce(cells, Quantizer.Map(cells, palette), palette, 0));
    }

    [Fact]
    public void ACompactFeatureOutlivesAScatteredShadeOfTheSameSize()
    {
        // A line of 40 cells: black, then a solid block of red, then white — and six green cells
        // dropped in one at a time, each on its own. Red and green are both six cells; without
        // connectivity the tie breaks on the higher palette index and red, the feature, is lost.
        Palette palette = PaletteFactory.OfHex("#000000", "#FFFFFF", "#00CC00", "#DD0000");

        var hex = new List<string>();
        for (int i = 0; i < 40; i++)
        {
            hex.Add(i < 17 ? "#000000" : i < 23 ? "#DD0000" : "#FFFFFF");
        }

        foreach (int scattered in new[] { 2, 6, 10, 27, 31, 35 })
        {
            hex[scattered] = "#00CC00";
        }

        LinearRgb[] cells = [.. hex.Select(h => Rgb.FromHex(h).ToLinear())];
        CieLab[] cellLab = Quantizer.ToLab(cells);
        int[] indices = Quantizer.Map(cells, palette);

        Tessera[] tesserae = [.. Enumerable.Range(0, 40).Select(LineCell)];

        ReductionOutcome withConnectivity = PaletteReducer.Reduce(
            cellLab, [.. indices], PaletteObservation.Lab(palette), maxColors: 3, pinned: null, tesserae);
        ReductionOutcome without = PaletteReducer.Reduce(
            cellLab, [.. indices], PaletteObservation.Lab(palette), maxColors: 3, pinned: null);

        Assert.Contains(3, withConnectivity.Indices);      // the compact red block is kept
        Assert.DoesNotContain(2, withConnectivity.Indices); // the scattered green is dropped
        Assert.NotEqual(without.Indices, withConnectivity.Indices);
    }

    [Fact]
    public void WithoutANeighbourhoodAnOrphanGoesToItsOwnNearestSurvivor()
    {
        (Palette palette, LinearRgb[] cells, Tessera[] tesserae, int centre) = OrphanInABlockOfB();
        int[] indices = Quantizer.Map(cells, palette);

        // No neighbourhood argument: the centre cell is re-quantised from its own colour, which
        // sits nearer A than B.
        ReductionOutcome outcome = PaletteReducer.Reduce(
            Quantizer.ToLab(cells), [.. indices], PaletteObservation.Lab(palette),
            maxColors: 2, pinned: null, tesserae);

        Assert.Equal(0, outcome.Indices[centre]);   // A
    }

    [Fact]
    public void AnOrphanRingedByOneSurvivorFollowsItEvenWhenItsOwnColourLeansTheOtherWay()
    {
        (Palette palette, LinearRgb[] cells, Tessera[] tesserae, int centre) = OrphanInABlockOfB();
        int[] indices = Quantizer.Map(cells, palette);
        var hood = CellNeighbourhood.Build(tesserae, 15.0);

        ReductionOutcome outcome = PaletteReducer.Reduce(
            Quantizer.ToLab(cells), [.. indices], PaletteObservation.Lab(palette),
            maxColors: 2, pinned: null, tesserae, hood);

        Assert.Equal(2, outcome.Indices[centre]);   // B — the shade every neighbour carries
        Assert.Equal(2, outcome.Indices.Distinct().Count());
        Assert.Equal(cells.Length, outcome.Indices.Length);
    }

    [Fact]
    public void TheOrphanHandOutIsDeterministic()
    {
        (Palette palette, LinearRgb[] cells, Tessera[] tesserae, _) = OrphanInABlockOfB();
        int[] indices = Quantizer.Map(cells, palette);
        var hood = CellNeighbourhood.Build(tesserae, 15.0);

        ReductionOutcome a = PaletteReducer.Reduce(
            Quantizer.ToLab(cells), [.. indices], PaletteObservation.Lab(palette),
            maxColors: 2, pinned: null, tesserae, hood);
        ReductionOutcome b = PaletteReducer.Reduce(
            Quantizer.ToLab(cells), [.. indices], PaletteObservation.Lab(palette),
            maxColors: 2, pinned: null, tesserae, hood);

        Assert.Equal(a.Indices, b.Indices);
    }

    /// <summary>
    /// A 7×7 grid: an A border, a B interior, and one near-neutral centre cell whose own colour
    /// leans to A but which is walled in by B. Reducing to two shades drops the centre.
    /// </summary>
    private static (Palette, LinearRgb[], Tessera[], int Centre) OrphanInABlockOfB()
    {
        Palette palette = PaletteFactory.OfHex("#808080", "#858585", "#909090");   // A, M, B

        var hex = new string[49];
        for (int row = 0; row < 7; row++)
        {
            for (int col = 0; col < 7; col++)
            {
                bool border = row == 0 || row == 6 || col == 0 || col == 6;
                hex[(row * 7) + col] = border ? "#808080" : "#909090";
            }
        }

        int centre = (3 * 7) + 3;
        hex[centre] = "#858585";

        LinearRgb[] cells = [.. hex.Select(h => Rgb.FromHex(h).ToLinear())];
        Tessera[] tesserae = [.. Enumerable.Range(0, 49).Select(GridCell)];
        return (palette, cells, tesserae, centre);
    }

    private static Tessera GridCell(int i)
    {
        int row = i / 7;
        int col = i % 7;
        double x = col * 10.0;
        double y = row * 10.0;
        return new Tessera
        {
            Polygon = [new(x, y), new(x + 8, y), new(x + 8, y + 8), new(x, y + 8)],
            Centroid = new PointD(x + 4, y + 4),
            AreaMm2 = 64,
            CourseId = row,
            IndexInCourse = col,
            IsCut = false,
        };
    }

    private static Tessera LineCell(int i) => new()
    {
        Polygon = [new(i * 10, 0), new((i * 10) + 8, 0), new((i * 10) + 8, 8), new(i * 10, 8)],
        Centroid = new PointD((i * 10) + 4, 4),
        AreaMm2 = 64,
        CourseId = 0,
        IndexInCourse = i,
        IsCut = false,
    };

    private static LinearRgb[] Cells(Palette palette, int[] counts) =>
        [.. counts.SelectMany((count, i) => Enumerable.Repeat(palette.Colors[i].Rgb.ToLinear(), count))];

    private static IEnumerable<LinearRgb> Repeat(string hex, int count) =>
        Enumerable.Repeat(Rgb.FromHex(hex).ToLinear(), count);
}
