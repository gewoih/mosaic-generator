namespace MosaicGenerator.Core.Domain;

public sealed record ModuleChoice
{
    /// <summary>Length of the piece along its course — the length of the bite, the mosaicist's choice.</summary>
    public required double ModuleAlongMm { get; init; }

    /// <summary>
    /// Width of the piece across its course. Set cut-face up, the break runs through the plate, so
    /// this side is the plate's own thickness and no choice of the mosaicist's can shorten it.
    /// </summary>
    public required double ModuleAcrossMm { get; init; }

    public required double GroutMm { get; init; }

    public required int ModulesAcrossShortSide { get; init; }

    public required int TotalModules { get; init; }
}

/// <summary>
/// Turns a panel size, a plate thickness and the mosaicist's chosen bite length into the rectangle
/// that will actually be cut, instead of leaving the arithmetic to be redone by hand.
///
/// The piece is a rectangle, not a square, and only one of its sides is a choice. Smalt is set with
/// the cut face up — the fracture is what carries the gloss and the clean body colour — and the
/// fracture runs through the plate, so one side of every face is the plate's thickness. What the
/// mosaicist decides is the other side: how long a bite to take along the course. The bite may be
/// shorter than the plate is thick — a 7 mm plate gives 7x6 pieces readily enough — so the only
/// floor on it is what a hand can place.
///
/// The plate still caps how fine the work can go, but through the other axis: courses cannot be set
/// closer together than the plate is thick, however short the bite.
///
/// That is also why the joint is measured against the plate: the thickness is the one dimension the
/// material fixes, so it is the honest yardstick for how wide a joint reads.
///
/// The bite chosen here is a floor, not the size of every piece on the panel: courses on a calm,
/// low-detail field are stretched longer by the tessellation itself (up to twice this length), and
/// only courses that follow a real edge — a silhouette, a contour — are cut at exactly this length.
/// See <c>Tessellation.ResizedAlong</c>.
/// </summary>
public static class ModuleSelector
{
    /// <summary>
    /// Bite lengths a mosaicist actually cuts, finest to coarsest. Six millimetres is the floor
    /// because it is the shortest bite that comes out on purpose rather than by accident — smaller
    /// pieces do happen, but a panel cannot be set out of them.
    /// </summary>
    public static readonly double[] AvailableModulesMm = [6, 8, 10, 12, 15, 20];

    /// <summary>
    /// Plate thickness to fall back on before a palette has been resolved — only reached while a
    /// form is being validated, never in a generated layout, where the palette always answers.
    /// </summary>
    public const double DefaultPlateThicknessMm = 7.0;

    /// <summary>
    /// Joint as a share of the plate. Set from real work rather than from a rule of thumb: a 6–7 mm
    /// plate is routinely laid with a joint of exactly 1 mm, and 0.15 is what reproduces that.
    /// </summary>
    private const double GroutRatio = 0.15;

    /// <summary>
    /// The tightest joint a hand can hold on a bed of adhesive. Measured on real work: joints run
    /// 1–2 mm, occasionally 3, and below about 0.7 the piece is being pushed into its neighbour
    /// rather than set beside it.
    /// </summary>
    private const double MinGroutMm = 0.7;

    private const double MaxGroutMm = 3.0;

    /// <summary>Joint proportional to the tessera, rounded to half a millimetre and kept practical.</summary>
    public static double GroutFor(double moduleMm) => Math.Clamp(
        Math.Round(moduleMm * GroutRatio * 2.0, MidpointRounding.AwayFromZero) / 2.0,
        MinGroutMm,
        MaxGroutMm);

    public static ModuleChoice Choose(
        double panelWidthMm,
        double panelHeightMm,
        double moduleAlongMm,
        double plateThicknessMm)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(panelWidthMm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(panelHeightMm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(moduleAlongMm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(plateThicknessMm);

        double across = plateThicknessMm;
        double grout = GroutFor(across);
        double shortSide = Math.Min(panelWidthMm, panelHeightMm);

        int columns = MosaicLayout.FitCount(panelWidthMm, moduleAlongMm, grout);
        int rows = MosaicLayout.FitCount(panelHeightMm, across, grout);

        return new ModuleChoice
        {
            ModuleAlongMm = moduleAlongMm,
            ModuleAcrossMm = across,
            GroutMm = grout,
            ModulesAcrossShortSide = MosaicLayout.FitCount(shortSide, moduleAlongMm, grout),
            TotalModules = columns * rows,
        };
    }
}
