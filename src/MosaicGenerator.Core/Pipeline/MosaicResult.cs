using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Grid;
using MosaicGenerator.Core.Material;
using MosaicGenerator.Core.Rendering;

namespace MosaicGenerator.Core.Pipeline;

public sealed record MosaicResult
{
    public required byte[] CartoonPng { get; init; }

    public required byte[] SchemePng { get; init; }

    /// <summary>Legend sheet — swatch, number, code and count per article — printed separately.</summary>
    public required byte[] LegendPng { get; init; }

    public required MaterialReport Report { get; init; }

    public required MosaicLayout Layout { get; init; }

    public required Palette Palette { get; init; }

    public required RenderPlan Cartoon { get; init; }

    /// <summary>Height of the cartoon PNG — the panel raster plus the scale-bar strip beneath it.</summary>
    public required int CartoonSheetHeightPx { get; init; }

    public required RenderPlan Scheme { get; init; }

    /// <summary>Shades the quantiser picked before the layout was trimmed to the colour ceiling.</summary>
    public required int ColorsBeforeReduction { get; init; }

    /// <summary>
    /// Shades the knee search settled on for this panel — the number of colours a further shade
    /// dropped starts costing the subject rather than the background.
    /// </summary>
    public required int AutoColors { get; init; }

    /// <summary>
    /// Shades actually rendered: <see cref="AutoColors"/>, unless the mosaicist forced an exact
    /// count from the result page.
    /// </summary>
    public required int ChosenColors { get; init; }

    /// <summary>The colour ceiling from the request — the automatic pick never exceeds it.</summary>
    public required int ColorCeiling { get; init; }

    /// <summary>
    /// The ratio the knee search decided on: the first drop dearer than
    /// <see cref="Quantization.ReductionLadder.KneeFactor"/> times the running median. Below the
    /// factor when no knee was found and the pick fell back to the ceiling.
    /// </summary>
    public required double KneeRatio { get; init; }

    /// <summary>
    /// A cartoon and a material table per shade count around <see cref="ChosenColors"/>, so the
    /// result page can step through them with no round trip. Empty when the ladder was not baked.
    /// </summary>
    public required IReadOnlyList<ColorLadderRung> ColorLadder { get; init; }

    /// <summary>
    /// Diagnostic (TODO п. 18): the full reduction cost curve — the marginal cost of every drop from
    /// the shades in use down to the ladder floor. The signal the knee search runs on; dumped by the
    /// bench with <c>--ladder</c> to calibrate <see cref="Quantization.ReductionLadder.KneeFactor"/>.
    /// </summary>
    public required IReadOnlyList<ColorLadderCost> LadderCurve { get; init; }

    /// <summary>Modules that ended up on a different shade than the quantiser first chose.</summary>
    public required int ModulesReassigned { get; init; }

    /// <summary>
    /// Diagnostic (TODO п. 13): modules the settling pass after the reduction moved. Measures how
    /// much work that second pass still has to do once the reducer hands out orphans coherently.
    /// </summary>
    public required int SettledAfterReduction { get; init; }

    /// <summary>
    /// Diagnostic (TODO п. 13): of <see cref="SettledAfterReduction"/>, those the reducer had also
    /// moved. The rest were never orphans, so their disagreement was not created by the hand-out —
    /// it appeared because the shades around them changed.
    /// </summary>
    public required int SettledAfterReductionOnMoved { get; init; }

    /// <summary>Pinned articles alone exceeded the colour ceiling, so the ceiling gave way.</summary>
    public required bool StoppedAtPinnedColors { get; init; }

    /// <summary>How many tesserae the layout holds. No longer a plain grid count.</summary>
    public required int TesseraCount { get; init; }

    /// <summary>Of those, how many are partial — clipped by the field edge or a contour.</summary>
    public required int CutTesseraCount { get; init; }

    /// <summary>
    /// The tesserae of the field, in the order <see cref="FinalIndices"/> and the render list use.
    /// Kept so the result page can re-colour an article without re-running the whole pipeline —
    /// see <see cref="MosaicGenerationService.Recolor"/>.
    /// </summary>
    public required IReadOnlyList<Tessera> Tesserae { get; init; }

    /// <summary>
    /// Palette index per tessera at the chosen shade count, after the settling pass that follows
    /// the reduction — the base a manual article swap is applied to.
    /// </summary>
    public required IReadOnlyList<int> FinalIndices { get; init; }
}

/// <summary>
/// One step of the colour ladder as the result page needs it: the cartoon at that shade count and
/// the material table that goes with it. The numbered scheme and the legend are not baked — they
/// carry per-shade numbers, so stepping the count needs a full regeneration for those.
/// </summary>
public sealed record ColorLadderRung
{
    public required int ColorCount { get; init; }

    public required byte[] CartoonPng { get; init; }

    public required int CartoonSheetHeightPx { get; init; }

    public required MaterialReport Report { get; init; }

    /// <summary>Modules on a different shade than the quantiser first chose, at this rung.</summary>
    public required int ModulesReassigned { get; init; }
}

/// <summary>One rung of the reduction cost curve, numbers only — see <see cref="MosaicResult.LadderCurve"/>.</summary>
public sealed record ColorLadderCost
{
    public required int ColorCount { get; init; }

    /// <summary>Perceptual cost of the drop that produced this rung; zero for the top rung.</summary>
    public required double MarginalCost { get; init; }

    public required int ModulesReassigned { get; init; }
}
