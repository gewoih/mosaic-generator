using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Quantization;
using MosaicGenerator.Core.Tests.Support;

namespace MosaicGenerator.Core.Tests.Quantization;

/// <summary>
/// The ladder is the whole cost curve of the greedy reduction in one pass, and the knee search
/// reads it to pick the shade count where a further colour dropped starts costing the subject
/// rather than the background.
/// </summary>
public class ReductionLadderTests
{
    // Ten greys jittered around one point, so any of them costs about the same to give up however
    // many are left — a plateau, not a ramp — and four saturated shades kept well apart, each
    // carrying a compact block of cells: the cliff at the end of the plateau.
    private static readonly string[] CloseGreys =
        ["#3F4041", "#40403F", "#413F40", "#3F413F", "#40413F",
         "#413F41", "#3F3F41", "#41413F", "#403F41", "#3F4141"];

    private static readonly string[] Saturated =
        ["#C81E1E", "#1EA01E", "#2038C0", "#C8B41E"];

    [Fact]
    public void TheLadderRunsFromTheShadesInUseDownToTheFloor()
    {
        int[] indices = Layout(out CieLab[] cellLab, out CieLab[] paletteLab);

        ReductionLadder ladder = PaletteReducer.BuildLadder(cellLab, indices, paletteLab, floor: 2, pinned: null);

        Assert.Equal(indices.Distinct().Count(), ladder.Rungs[0].ColorCount);
        Assert.Equal(2, ladder.Floor.ColorCount);
        for (int i = 1; i < ladder.Rungs.Count; i++)
        {
            Assert.Equal(ladder.Rungs[i - 1].ColorCount - 1, ladder.Rungs[i].ColorCount);
        }

        Assert.Equal(0.0, ladder.Rungs[0].MarginalCost);
        Assert.All(ladder.Rungs.Skip(1), rung => Assert.True(rung.MarginalCost > 0.0));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    public void EachRungIsWhatAStandaloneReduceToThatCountWouldProduce(int colors)
    {
        int[] indices = Layout(out CieLab[] cellLab, out CieLab[] paletteLab);

        ReductionLadder ladder = PaletteReducer.BuildLadder(cellLab, indices, paletteLab, floor: 2, pinned: null);
        ReductionOutcome standalone = PaletteReducer.Reduce(
            cellLab, [.. indices], paletteLab, colors, pinned: null);

        Assert.Equal(standalone.Indices, ladder.RungAt(colors).Indices);
        Assert.Equal(standalone.ModulesReassigned, ladder.RungAt(colors).ModulesReassigned);
    }

    [Fact]
    public void TheKneeStopsWhileTheLoadBearingShadesAreStillStanding()
    {
        int[] indices = Layout(out CieLab[] cellLab, out CieLab[] paletteLab);
        int[] saturated = [.. Enumerable.Range(CloseGreys.Length, Saturated.Length)];

        ReductionLadder ladder = PaletteReducer.BuildLadder(cellLab, indices, paletteLab, floor: 2, pinned: null);
        ColorLadderPick pick = ladder.Knee(ceiling: 16);

        // Some greys are shed, but the pick stops above the floor and keeps every saturated shade —
        // those are the ones with nowhere cheap to go.
        Assert.InRange(pick.Colors, 4, ladder.Rungs[0].ColorCount - 1);
        Assert.True(pick.KneeRatio > ReductionLadder.KneeFactor, $"ratio {pick.KneeRatio}");
        Assert.All(saturated, s => Assert.Contains(s, ladder.RungAt(pick.Colors).RetainedColors));
    }

    [Fact]
    public void ThePickNeverExceedsTheCeiling()
    {
        int[] indices = Layout(out CieLab[] cellLab, out CieLab[] paletteLab);
        ReductionLadder ladder = PaletteReducer.BuildLadder(cellLab, indices, paletteLab, floor: 2, pinned: null);

        // The knee sits above three shades; a ceiling of three pulls the pick down to it.
        Assert.Equal(3, ladder.Knee(ceiling: 3).Colors);
    }

    [Fact]
    public void TooFewDropsToJudgeFallsBackToTheCeiling()
    {
        // Six well-separated shades, floor at four: only two drops — the knee search never gets the
        // three rungs it needs to take a median, so the pick is the ceiling.
        Palette palette = PaletteFactory.OfHex(
            "#101010", "#403030", "#306030", "#303090", "#909030", "#D0D0D0");
        LinearRgb[] cells =
        [
            .. Enumerable.Range(0, palette.Colors.Count).SelectMany(shade =>
                Enumerable.Repeat(palette.Colors[shade].Rgb.ToLinear(), 12)),
        ];
        CieLab[] cellLab = Quantizer.ToLab(cells);
        CieLab[] paletteLab = PaletteObservation.Lab(palette);
        int[] indices = Quantizer.Map(cellLab, paletteLab);

        ReductionLadder ladder = PaletteReducer.BuildLadder(cellLab, indices, paletteLab, floor: 4, pinned: null);
        ColorLadderPick pick = ladder.Knee(ceiling: 5);

        Assert.Equal(5, pick.Colors);
        Assert.Equal(0.0, pick.KneeRatio);
    }

    [Fact]
    public void PinnedShadesStopTheWalkAndTheFloorRungHoldsThemAll()
    {
        int[] indices = Layout(out CieLab[] cellLab, out CieLab[] paletteLab);
        var pins = new HashSet<int> { 0, 1, 2, 3, 4 };

        ReductionLadder ladder = PaletteReducer.BuildLadder(cellLab, indices, paletteLab, floor: 2, pinned: pins);

        Assert.True(ladder.StoppedAtPinnedColors);
        Assert.Equal(5, ladder.Floor.ColorCount);
        Assert.All(pins, pin => Assert.Contains(pin, ladder.Floor.RetainedColors));
    }

    /// <summary>Ten close greys and four saturated blocks — fourteen shades, the greys cheap to shed.</summary>
    private static int[] Layout(out CieLab[] cellLab, out CieLab[] paletteLab)
    {
        Palette palette = PaletteFactory.OfHex([.. CloseGreys, .. Saturated]);

        var cells = new List<LinearRgb>();
        foreach (string grey in CloseGreys)
        {
            cells.AddRange(Enumerable.Repeat(Rgb.FromHex(grey).ToLinear(), 24));
        }

        foreach (string shade in Saturated)
        {
            cells.AddRange(Enumerable.Repeat(Rgb.FromHex(shade).ToLinear(), 10));
        }

        LinearRgb[] all = [.. cells];
        cellLab = Quantizer.ToLab(all);
        paletteLab = PaletteObservation.Lab(palette);
        return Quantizer.Map(cellLab, paletteLab);
    }
}
