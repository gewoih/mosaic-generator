using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Quantization;
using MosaicGenerator.Core.Tests.Support;

namespace MosaicGenerator.Core.Tests.Quantization;

/// <summary>
/// The reduction drops shades by what losing them costs, not by how rare they are. A picture is
/// often carried by a few tesserae — a beak, a catchlight, one red accent — and counting alone
/// throws exactly those away first.
/// </summary>
public class ReductionCostTests
{
    // Four close greys plus one saturated orange that appears a handful of times.
    private static readonly string[] Shades =
        ["#000000", "#1A1A1A", "#343434", "#4E4E4E", "#FF6A00"];

    [Fact]
    public void TheAccentIsTheRarestShadeInThisLayout()
    {
        int[] indices = Mapped(out _, out _);

        int orange = indices.Count(i => i == 4);
        Assert.Equal(4, orange);
        Assert.All(
            Enumerable.Range(0, 4),
            grey => Assert.True(indices.Count(i => i == grey) > orange));
    }

    [Fact]
    public void ARareButIsolatedAccentSurvivesWhileACrowdedGreyGoes()
    {
        int[] indices = Mapped(out CieLab[] cellLab, out CieLab[] paletteLab);

        ReductionOutcome outcome = PaletteReducer.Reduce(cellLab, indices, paletteLab, maxColors: 4, pinned: null);

        Assert.Equal(4, outcome.ColorsAfter);
        Assert.Contains(4, outcome.Indices);
    }

    [Fact]
    public void TheShadeGivenUpIsAGreyWithNeighboursNotTheIsolatedAccent()
    {
        int[] indices = Mapped(out CieLab[] cellLab, out CieLab[] paletteLab);

        ReductionOutcome outcome = PaletteReducer.Reduce(cellLab, indices, paletteLab, maxColors: 4, pinned: null);

        int dropped = Dropped(indices, outcome);

        // The accent is the rarest shade of the five, so dropping by count would have taken it.
        Assert.Equal(4, indices.GroupBy(i => i).MinBy(group => group.Count())!.Key);
        Assert.NotEqual(4, dropped);
    }

    [Fact]
    public void APinnedArticleIsKeptEvenWhenItIsTheCheapestToLose()
    {
        // A grey wedged between two neighbours costs almost nothing to drop, which is exactly why
        // it has to be pinnable: cheap to lose is not the same as unwanted.
        int[] indices = Mapped(out CieLab[] cellLab, out CieLab[] paletteLab);

        int wouldGo = Dropped(
            indices, PaletteReducer.Reduce(cellLab, indices, paletteLab, maxColors: 4, pinned: null));

        ReductionOutcome pinnedRun = PaletteReducer.Reduce(
            cellLab, indices, paletteLab, maxColors: 4, pinned: new HashSet<int> { wouldGo });

        Assert.Contains(wouldGo, pinnedRun.Indices);
        Assert.Equal(4, pinnedRun.ColorsAfter);
        Assert.False(pinnedRun.StoppedAtPinnedColors);

        // Something else had to give instead.
        Assert.NotEqual(wouldGo, Dropped(indices, pinnedRun));
    }

    /// <summary>The one shade present before the reduction and absent after it.</summary>
    private static int Dropped(int[] before, ReductionOutcome outcome) =>
        before.Distinct().Except(outcome.Indices.Distinct()).Single();

    [Fact]
    public void PinsOutrankTheCeilingWhenTheTwoCannotBothBeHonoured()
    {
        int[] indices = Mapped(out CieLab[] cellLab, out CieLab[] paletteLab);

        // Four pins against a ceiling of two: the ceiling is a rule of thumb, a pin is an order.
        ReductionOutcome outcome = PaletteReducer.Reduce(
            cellLab, indices, paletteLab, maxColors: 2, pinned: new HashSet<int> { 0, 1, 2, 3 });

        Assert.True(outcome.StoppedAtPinnedColors);
        Assert.Equal(4, outcome.ColorsAfter);
        Assert.DoesNotContain(4, outcome.Indices);
    }

    private static int[] Mapped(out CieLab[] cellLab, out CieLab[] paletteLab)
    {
        Palette palette = PaletteFactory.OfHex(Shades);

        LinearRgb[] cells =
        [
            .. Enumerable.Range(0, 4).SelectMany(shade =>
                Enumerable.Repeat(Rgb.FromHex(Shades[shade]).ToLinear(), 20)),
            .. Enumerable.Repeat(Rgb.FromHex("#FF6A00").ToLinear(), 4),
        ];

        cellLab = Quantizer.ToLab(cells);
        paletteLab = PaletteObservation.Lab(palette);
        return Quantizer.Map(cellLab, paletteLab);
    }
}
