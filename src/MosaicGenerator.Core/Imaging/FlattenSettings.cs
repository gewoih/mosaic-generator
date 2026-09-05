namespace MosaicGenerator.Core.Imaging;

/// <summary>
/// How hard the photograph is flattened before it is sampled, and how directionally.
/// <c>RadiusTesserae</c> is the reach along the form, in tesserae;
/// <c>AcrossFraction</c> is how much of that reach survives across it, where the
/// tensor is sure of a direction — 1,0 makes the kernel round again.
/// See docs/ploskosti-spike.md and docs/anizotropnoe-uploshchenie-plan.md.
/// </summary>
public sealed record FlattenSettings(
    double RadiusTesserae, double RangeDe, int Iterations, double AcrossFraction = 0.25)
{
    /// <summary>
    /// What the web actually runs, and therefore what the bench measures unless told otherwise.
    /// Kept here rather than written out twice: a bench whose default quietly differs from
    /// production measures a pipeline nobody uses, and every stage below it is then judged
    /// against a signal it never sees. Radius held at 0,7 because past it the filter smears
    /// across the boundary between grey and beige — see docs/anizotropnoe-uploshchenie-plan.md.
    /// </summary>
    public static readonly FlattenSettings Production =
        new(RadiusTesserae: 0.7, RangeDe: 6.0, Iterations: 2, AcrossFraction: 0.25);
}
