using MosaicGenerator.Core.Colors;

namespace MosaicGenerator.Core.Rendering;

public sealed class RenderedModule
{
    /// <summary>The course that laid this tessera. Straight rows for the nominal grid, streamlines otherwise.</summary>
    public required int Row { get; init; }

    /// <summary>Position along the course.</summary>
    public required int Column { get; init; }

    public required int ColorIndex { get; init; }

    /// <summary>A partial tessera — clipped by the field edge or squeezed where courses converge.</summary>
    public required bool IsCut { get; init; }

    /// <summary>Axis-aligned bounds of the nominal outline in output pixels, before shaping.</summary>
    public required RectD Bounds { get; init; }

    /// <summary>Centre of the nominal outline in output pixels.</summary>
    public required PointD Centroid { get; init; }

    /// <summary>Shaped outline in output pixels: chipped, resized and rotated off-square.</summary>
    public required PointD[] Quad { get; init; }

    /// <summary>Palette colour with this module's tone jitter applied — the face's mid value.</summary>
    public required Rgb FillColor { get; init; }

    /// <summary>Shaded edge of the glossy face. Equal to <see cref="FillColor"/> when gloss is off.</summary>
    public required Rgb GlossLow { get; init; }

    /// <summary>Lit edge of the glossy face. Equal to <see cref="FillColor"/> when gloss is off.</summary>
    public required Rgb GlossHigh { get; init; }
}
