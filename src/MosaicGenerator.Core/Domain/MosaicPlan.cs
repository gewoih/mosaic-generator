using MosaicGenerator.Core.Grid;

namespace MosaicGenerator.Core.Domain;

/// <summary>The finished layout: which palette colour lands on every tessera of the field.</summary>
public sealed class MosaicPlan
{
    private IReadOnlyList<Tessera>? _tesserae;

    public MosaicPlan(
        MosaicLayout layout,
        Palette palette,
        int[] colorIndices,
        ulong seed,
        IReadOnlyList<Tessera>? tesserae = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(colorIndices);

        // With a tessellation supplied the count is whatever it holds; without one the plan falls
        // back to the nominal grid, which is exactly TotalModules.
        int expected = tesserae?.Count ?? layout.TotalModules;
        if (colorIndices.Length != expected)
        {
            throw new ArgumentException(
                $"Expected {expected} colour indices, got {colorIndices.Length}.", nameof(colorIndices));
        }

        Layout = layout;
        Palette = palette;
        ColorIndices = colorIndices;
        Seed = seed;
        _tesserae = tesserae;
    }

    public MosaicLayout Layout { get; }

    public Palette Palette { get; }

    /// <summary>Palette index per tessera, in the order <see cref="Tesserae"/> lists them.</summary>
    public int[] ColorIndices { get; }

    public ulong Seed { get; }

    /// <summary>
    /// The tesserae themselves. Supplied by the pipeline once the direction field is known;
    /// falls back to a plain staggered grid so a plan built straight from a layout still renders.
    /// </summary>
    public IReadOnlyList<Tessera> Tesserae => _tesserae ??= Tessellation.NominalGrid(Layout);

    public PaletteColor ColorAt(int tesseraIndex) => Palette.Colors[ColorIndices[tesseraIndex]];
}
