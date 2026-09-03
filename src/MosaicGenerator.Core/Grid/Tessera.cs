using MosaicGenerator.Core.Rendering;

namespace MosaicGenerator.Core.Grid;

/// <summary>
/// One tessera as it sits in the field: an oriented outline in field millimetres (origin at the
/// field's top-left corner), the course it belongs to, and whether it had to be cut to fit.
/// This is the nominal shape — the per-tessera chipping and rotation of the preview are added on
/// top of it at render time.
/// </summary>
public sealed record Tessera
{
    /// <summary>Outline in field millimetres, clockwise, usually four points.</summary>
    public required PointD[] Polygon { get; init; }

    public required PointD Centroid { get; init; }

    public required double AreaMm2 { get; init; }

    /// <summary>Which course laid this tessera. Straight rows for the nominal grid, streamlines otherwise.</summary>
    public required int CourseId { get; init; }

    /// <summary>Position along the course, from its start.</summary>
    public required int IndexInCourse { get; init; }

    /// <summary>A partial tessera: clipped by the field edge or squeezed where courses converge.</summary>
    public required bool IsCut { get; init; }
}
