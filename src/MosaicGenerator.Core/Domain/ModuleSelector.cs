namespace MosaicGenerator.Core.Domain;

public enum ModuleLimit
{
    None,

    /// <summary>The finest module still falls short of the target: the panel is too small.</summary>
    PanelTooSmall,

    /// <summary>A finer module was rejected because the layout would exceed the module ceiling.</summary>
    ModuleCountCapped,

    /// <summary>
    /// A finer module was refused because the plate is thicker than that. Smalt set cut-face up
    /// cannot give a course narrower than the plate it was broken from.
    /// </summary>
    PlateThickness,
}

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

    public required int RequestedAcross { get; init; }

    public required int TotalModules { get; init; }

    public required ModuleLimit Limit { get; init; }

    public bool ReachedTarget => Limit == ModuleLimit.None;
}

/// <summary>
/// Picks a realistic tessera and joint for a panel instead of making the user hunt for the pair by
/// trial and error.
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
        DetailLevel level,
        int maxModules,
        double plateThicknessMm)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(panelWidthMm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(panelHeightMm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxModules);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(plateThicknessMm);

        double across = plateThicknessMm;
        double grout = GroutFor(across);
        double shortSide = Math.Min(panelWidthMm, panelHeightMm);
        int target = level.TargetAcross();

        ModuleChoice? best = null;
        bool rejectedByCount = false;
        double finestUsable = double.MaxValue;

        // How many courses the plate allows across the short side. However short the bite, they cannot
        // be set closer than this, so it is the plate's own ceiling on detail.
        int coursesAcross = MosaicLayout.FitCount(shortSide, across, grout);

        foreach (double along in AvailableModulesMm)
        {
            int columns = MosaicLayout.FitCount(panelWidthMm, along, grout);
            int rows = MosaicLayout.FitCount(panelHeightMm, across, grout);
            if (columns < 1 || rows < 1)
            {
                continue;
            }

            long total = (long)columns * rows;
            if (total > maxModules)
            {
                rejectedByCount = true;
                continue;
            }

            finestUsable = Math.Min(finestUsable, along);

            var candidate = new ModuleChoice
            {
                ModuleAlongMm = along,
                ModuleAcrossMm = across,
                GroutMm = grout,
                ModulesAcrossShortSide = MosaicLayout.FitCount(shortSide, along, grout),
                RequestedAcross = target,
                TotalModules = (int)total,
                Limit = ModuleLimit.None,
            };

            // Closest to the requested count. Ties go to the finer module already held in `best`,
            // since falling short of the requested detail is the failure worth avoiding.
            if (best is null || Math.Abs(candidate.ModulesAcrossShortSide - target)
                    < Math.Abs(best.ModulesAcrossShortSide - target))
            {
                best = candidate;
            }
        }

        if (best is null)
        {
            // Nothing fitted under the ceiling. With the width across the course pinned to the plate,
            // a large panel simply needs more pieces than the ceiling allows, and no bite length can
            // change that. Hand back the coarsest bite there is and say what happened: the request
            // validator will refuse it with a message, which beats throwing out of a form.
            best = Coarsest(panelWidthMm, panelHeightMm, across, grout, target);
        }

        return best.Limit == ModuleLimit.ModuleCountCapped
            ? best
            : best with { Limit = Diagnose(best, rejectedByCount, coursesAcross, finestUsable) };
    }

    private static ModuleChoice Coarsest(
        double panelWidthMm, double panelHeightMm, double across, double grout, int target)
    {
        double along = Math.Max(across, AvailableModulesMm[^1]);
        int columns = MosaicLayout.FitCount(panelWidthMm, along, grout);
        int rows = MosaicLayout.FitCount(panelHeightMm, across, grout);
        if (columns < 1 || rows < 1)
        {
            throw new ArgumentException(
                "The plate is thicker than the panel is wide.", nameof(panelWidthMm));
        }

        return new ModuleChoice
        {
            ModuleAlongMm = along,
            ModuleAcrossMm = across,
            GroutMm = grout,
            ModulesAcrossShortSide = MosaicLayout.FitCount(Math.Min(panelWidthMm, panelHeightMm), along, grout),
            RequestedAcross = target,
            TotalModules = columns * rows,
            Limit = ModuleLimit.ModuleCountCapped,
        };
    }

    /// <summary>
    /// Only a real dead end counts as a limit. Landing a few modules short because the size range is
    /// discrete is normal and says nothing about the panel — the flag is raised only when there was
    /// no finer bite left to try: the range ran out, the ceiling took it, or the plate is simply
    /// thicker than the piece the level asked for.
    /// </summary>
    private static ModuleLimit Diagnose(
        ModuleChoice choice, bool rejectedByCount, int coursesAcross, double finestUsable)
    {
        if (choice.ModulesAcrossShortSide >= choice.RequestedAcross
            && coursesAcross >= choice.RequestedAcross)
        {
            return ModuleLimit.None;
        }

        if (rejectedByCount)
        {
            return ModuleLimit.ModuleCountCapped;
        }

        if (choice.ModuleAlongMm > finestUsable)
        {
            return ModuleLimit.None;   // a finer bite was available and simply not wanted
        }

        // Already on the finest bite there is, and still short. Whichever axis is the tighter of the
        // two is the one to name: a thick plate spaces the courses out, a small panel runs the range
        // of bites out.
        return coursesAcross < choice.ModulesAcrossShortSide
            ? ModuleLimit.PlateThickness
            : ModuleLimit.PanelTooSmall;
    }
}
