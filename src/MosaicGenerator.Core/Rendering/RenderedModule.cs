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

    /// <summary>Axis-aligned bounds of the outline in output pixels.</summary>
    public required RectD Bounds { get; init; }

    /// <summary>Centre of the outline in output pixels.</summary>
    public required PointD Centroid { get; init; }

    /// <summary>The tessera outline in output pixels, clamped to the field.</summary>
    public required PointD[] Quad { get; init; }

    /// <summary>Flat article colour of this module.</summary>
    public required Rgb FillColor { get; init; }
}
