using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;
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
}
