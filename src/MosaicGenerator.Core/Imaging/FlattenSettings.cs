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
    /// against a signal it never sees.
    ///
    /// The radius is one tessera, and it is one tessera for the reason the whole stage exists:
    /// nothing finer than a piece of smalt can be laid, so nothing finer than a piece survives to
    /// be sampled — and nothing coarser is touched. It was 0,7 until 2026-09-05 on the reading that
    /// a longer reach smears across the boundary between grey and beige. That reading was checked
    /// over all ten photographs at four sizes and does not hold: the dolphin's pink back turns on
    /// and off with radius without a trend (clean at 0,7 / 0,85 / 1,2 on 21×21, striped at 1,0 and
    /// 1,5; the other way round on A4), because a near-neutral body sits on the boundary between a
    /// grey article and a beige one and any perturbation tips a band of tesserae over it. That is a
    /// palette-match cliff, not a property of the filter.
    ///
    /// Judged instead on what moves monotonically and on the cartoons: at one tessera the crumb in
    /// flat skies is gone and not one feature is lost — the gull's eye, the cat's nose, the poppy's
    /// stamens, the red of the second gull's gape all survive, and they do not at 1,5.
    /// See docs/zony-i-granitsy-plan.md.
    /// </summary>
    public static readonly FlattenSettings Production =
        new(RadiusTesserae: 1.0, RangeDe: 6.0, Iterations: 2, AcrossFraction: 0.25);
}
