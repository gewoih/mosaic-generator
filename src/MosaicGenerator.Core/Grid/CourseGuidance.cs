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
    /// The blend is linear in coherence, so the number sets the slope, not a gate: on a structured
    /// photograph the field's coherence reaches p75 ≈ 0.3–0.4, so the picture already carries much
    /// of the say there; on flat ground its coherence stays near zero and the contour echo leads.
    /// Re-swept over the eight photographs at 0.18, 0.25, 0.35 and 0.5 on 15×15 and 30×30 after the
    /// direction field's confidence was fixed to normalise against a frame percentile rather than
    /// its single strongest cell (<c>docs/pole-napravleniy-plan.md</c>). 0.35 sits at the knee:
    /// the photograph leads on 12–34 % of the panel (was 1–4 % before the fix) and courses hold
    /// together best. Lower and the layout frays — at 0.18 the portrait's background runs 55–60 %
    /// of its courses under three pieces. Following the photograph's own field more closely looks
    /// like the faithful thing to do and is not — in flat or softly modelled ground that field is
    /// smooth but arbitrary, and streamlines through it collide and stop, while the contour echo is
    /// evenly spaced by construction.
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
