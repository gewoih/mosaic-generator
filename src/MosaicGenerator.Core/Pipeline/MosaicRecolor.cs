using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Grid;
using MosaicGenerator.Core.Material;

namespace MosaicGenerator.Core.Pipeline;

/// <summary>
/// Everything <see cref="MosaicGenerationService.Recolor"/> needs to redraw a finished layout with
/// articles swapped: the geometry and the base mapping are given, so nothing upstream of the
/// palette index has to run again.
/// </summary>
public sealed record RecolorRequest
{
    public required MosaicLayout Layout { get; init; }

    public required Palette Palette { get; init; }

    public required IReadOnlyList<Tessera> Tesserae { get; init; }

    /// <summary>Palette index per tessera before any swap — the chosen rung's final mapping.</summary>
    public required IReadOnlyList<int> BaseIndices { get; init; }

    /// <summary>
    /// Palette index to replace with palette index. A key absent from the map is left as it is;
    /// chains are followed (X→Y, Y→Z gives X→Z).
    /// </summary>
    public required IReadOnlyDictionary<int, int> Swaps { get; init; }

    public required double WasteFactor { get; init; }

    public required decimal PricePerKgRub { get; init; }
}

/// <summary>The redrawn sheets and the material table after the swaps in <see cref="RecolorRequest"/>.</summary>
public sealed record RecolorResult
{
    public required byte[] CartoonPng { get; init; }

    public required byte[] SchemePng { get; init; }

    public required byte[] LegendPng { get; init; }

    public required int CartoonSheetHeightPx { get; init; }

    public required MaterialReport Report { get; init; }
}
