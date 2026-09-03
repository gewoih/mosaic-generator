using MosaicGenerator.Core.Rendering;

namespace MosaicGenerator.Core.Grid;

/// <summary>
/// The direction a course actually takes: the photograph's own texture where the structure tensor
/// is sure of one, the echo of the nearest silhouette where it is not. This is opus musivum — the
/// background repeating the form rather than sitting under it as a grid.
///
/// It is a type of its own rather than a closure inside <see cref="Tessellation"/> because it is
/// the field the layout is laid along, and anything that judges the layout has to be able to ask
/// it the same question the placer asked. Measuring a course against
/// <see cref="DirectionField.ThetaAt"/> instead compares it to a field it never followed, which
/// is how a third of a right angle of apparent disagreement turned out to be two different fields
/// being compared rather than a course going the wrong way.
/// </summary>
public sealed class CourseGuidance
{
    /// <summary>
    /// Tensor confidence at which the photograph's own texture takes the direction over entirely.
    /// Below it the two are mixed in proportion, so there is no seam where the echo hands over.
    ///
    /// It reads as a high bar — the field's coherence only reaches it in the top percent of cells —
    /// but that is not what it does. The blend is linear in coherence, so the number sets the slope,
    /// not a gate: at the median coherence of a photograph it still gives the picture about a
    /// quarter of the say. Swept over the eight photographs at 0.08, 0.12, 0.18, 0.25, 0.35, 0.5,
    /// 0.8 and 1.5, on 15×15 and 60×60. Everything above 0.35 is flat, and below 0.25 the layout
    /// comes apart: fillers 0.10 → 0.14, courses 11.4 → 7.8 pieces. Following the photograph's own
    /// field more closely looks like the faithful thing to do and is not — in flat or softly modelled
    /// ground that field is smooth but arbitrary, and streamlines through it collide and stop, while
    /// the contour echo is evenly spaced by construction. Measured on the previews too: at 0.12 the
    /// gull's sky loses its courses altogether and the portrait's face breaks up rather than
    /// resolving. See <c>docs/kursy-i-tsvet-plan.md</c>, step 3.
    /// </summary>
    public const double FullConfidence = 0.35;

    private readonly DirectionField _field;
    private readonly DistanceField _distance;

    private CourseGuidance(DirectionField field, DistanceField distance)
    {
        _field = field;
        _distance = distance;
    }

    /// <summary>The contours the background echoes — the long ones, in field millimetres.</summary>
    public IReadOnlyList<PointD[]> Steering { get; private init; } = [];

    /// <summary>Course direction in radians at field-normalised (<paramref name="u"/>, <paramref name="v"/>).</summary>
    public double ThetaAt(double u, double v)
    {
        (double tensorTheta, double coherence) = _field.TensorAt(u, v);
        double echoTheta = _distance.TangentAt(u, v);
        double w = Math.Clamp(coherence / FullConfidence, 0.0, 1.0);

        // Averaged as doubled angles: a course has an axis, not a heading, so 0 and π are the same
        // direction and must not average to a right angle.
        double x2 = ((1.0 - w) * Math.Cos(2.0 * echoTheta)) + (w * Math.Cos(2.0 * tensorTheta));
        double y2 = ((1.0 - w) * Math.Sin(2.0 * echoTheta)) + (w * Math.Sin(2.0 * tensorTheta));
        return 0.5 * Math.Atan2(y2, x2);
    }

    /// <summary>
    /// Builds the guidance over <paramref name="contours"/>, of which only the long ones steer the
    /// background: a feather or a highlight is a real edge and still gets its own courses, but a
    /// background echoing such a scrap would repeat the detail rather than the silhouette, and read
    /// busy and torn.
    /// </summary>
    public static CourseGuidance Build(
        DirectionField field,
        double fieldWidthMm,
        double fieldHeightMm,
        IReadOnlyList<PointD[]> contours,
        double alongMm,
        double acrossMm)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(contours);

        double steeringMin = Math.Min(alongMm * 20.0, Math.Max(fieldWidthMm, fieldHeightMm) * 0.25);
        var steering = contours.Where(c => Geometry.PolylineLength(c) >= steeringMin).ToList();
        if (steering.Count == 0 && contours.Count > 0)
        {
            steering.Add(contours[0]);   // ContourSet sorts longest first
        }

        DistanceField distance = DistanceField.Build(
            field.Width, field.Height, fieldWidthMm, fieldHeightMm, steering,
            tangentSmoothCells: acrossMm * (field.Width - 1) / fieldWidthMm);

        return new CourseGuidance(field, distance) { Steering = steering };
    }
}
