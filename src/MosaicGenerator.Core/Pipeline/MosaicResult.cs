using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Material;
using MosaicGenerator.Core.Rendering;

namespace MosaicGenerator.Core.Pipeline;

public sealed record MosaicResult
{
    public required byte[] CartoonPng { get; init; }

    public required byte[] SchemePng { get; init; }

    public required MaterialReport Report { get; init; }

    public required MosaicLayout Layout { get; init; }

    public required Palette Palette { get; init; }

    public required RenderPlan Cartoon { get; init; }

    public required RenderPlan Scheme { get; init; }

    /// <summary>Shades the quantiser picked before the layout was trimmed to the colour ceiling.</summary>
    public required int ColorsBeforeReduction { get; init; }

    /// <summary>Modules that ended up on a different shade than the quantiser first chose.</summary>
    public required int ModulesReassigned { get; init; }

    /// <summary>Pinned articles alone exceeded the colour ceiling, so the ceiling gave way.</summary>
    public required bool StoppedAtPinnedColors { get; init; }

    /// <summary>How many tesserae the layout holds. No longer a plain grid count.</summary>
    public required int TesseraCount { get; init; }

    /// <summary>Of those, how many are partial — clipped by the field edge or a contour.</summary>
    public required int CutTesseraCount { get; init; }
}
