using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Grid;

namespace MosaicGenerator.Core.Quantization;

/// <summary>
/// Draws the planes apart harder than the camera drew them, at the scale of a few tesserae.
///
/// A work in twelve shades has a tonal step of about 7 ΔE — the finest distinction the material can
/// carry. Measured on the landscape, the hazy ridge and the sky behind it differ by 4,9–6,5 ΔE:
/// less than one step. So they land on the same article and the ridge arrives as a flat blue-grey
/// band. Neither more tesserae nor more articles fixes that — both were measured and both came back
/// unchanged — because the separation is not in the photograph to begin with.
///
/// A mosaicist drawing the cartoon does not copy the photograph either: they push the far plane
/// down and the near one up on purpose, knowing the two would otherwise merge in glass. This is
/// that move and nothing more. Each cell is compared with the average of the two and a half modules
/// around it and pushed away from it.
///
/// Two limits, and both are load-bearing:
///
/// A dead band, because below about two and a half ΔE what separates a cell from its surroundings
/// is not the ridge, it is grain, ripple and the encoder's noise. Multiplying that is how the tone
/// stretch produced speckle before <see cref="CoherentMap"/> was written, so the noise floor is
/// subtracted before anything is multiplied. Measured: without it, banding on the landscape at A4
/// went from 1,6 % to 5,3 % and the sky was visibly speckled.
///
/// A ceiling, because a fir against bright sky is already separated by three tonal steps. Lifting
/// it further buys no distinction that is not already there and would ring a bright collar around
/// the silhouette — the halo of an unsharp mask, which on a wall reads as a mistake.
///
/// The blur weighs neighbours by distance alone, unlike <see cref="CellSmoother"/>, which weighs
/// them by colour as well. Weighing by colour here would zero the difference exactly at the
/// boundary between two planes — the one place the separation is needed.
///
/// Ordering. This runs after <see cref="CellSmoother"/>, because otherwise it is texture being
/// lifted rather than structure, and — this part was got wrong first and corrected by measurement —
/// after <see cref="ToneMap"/> rather than before it. The argument for going first was that the
/// stretch is what keeps the result inside the material's range. What the bench showed is that the
/// stretch also *multiplies* whatever it is handed, noise included: lifting first cost 2–4 points
/// of banding on all three photographs for the same recovered gradation. Lifting last, the dead
/// band is expressed in the units the work is finally judged in, and the same gradation costs
/// between nothing and 1,4 points. Colours pushed past the range are not a problem in practice: the
/// matcher takes the nearest shade, which is the end of the range, and the dominant share did not
/// grow. Figures in docs/lokalnyy-kontrast-plan.md.
/// </summary>
public static class LocalContrast
{
    /// <summary>
    /// How far the ring reaches, in modules. Wide enough that a plane several tesserae across is
    /// measured against its surroundings rather than against itself, narrow enough to stay a local
    /// correction and not a second tone stretch. Four modules was measured and is no better.
    /// </summary>
    public const double ReachInModules = 2.5;

    /// <summary>
    /// Difference from the surroundings below which nothing is lifted at all, in ΔE. Texture between
    /// neighbouring tesserae runs about this much; structure runs more.
    /// </summary>
    public const double DeadBandDeltaE = 2.5;

    /// <summary>How far a single cell may be moved, in ΔE. The halo brake.</summary>
    public const double MostLiftDeltaE = 8.0;

    /// <summary>
    /// How much of the surviving difference is added back. Not a setting: there is one right value
    /// for a given material, and it is found by measurement rather than by the person at the panel.
    /// Measured at 0,4 / 0,6 / 0,8 / 1,0 / 1,5 — past 0,6 the banding cost grows faster than the
    /// gradation recovered.
    /// </summary>
    public const double Amount = 0.6;

    /// <summary>Reach for <see cref="CellNeighbourhood"/>, from the layout's own step.</summary>
    public static double ReachFor(MosaicLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return Math.Max(layout.StepXMm, layout.StepYMm) * ReachInModules;
    }

    /// <summary>
    /// Pushes every cell away from the average of the ring around it.
    /// </summary>
    /// <param name="cells">One colour per tessera, already laid into the palette's range.</param>
    /// <param name="wide">
    /// Neighbours within <see cref="ReachInModules"/> modules, built with <see cref="ReachFor"/> —
    /// a wider ring than the one the smoother and the coherence pass share.
    /// </param>
    public static CieLab[] Lift(ReadOnlySpan<CieLab> cells, CellNeighbourhood wide)
    {
        ArgumentNullException.ThrowIfNull(wide);

        var lifted = new CieLab[cells.Length];

        for (int i = 0; i < cells.Length; i++)
        {
            CieLab self = cells[i];
            CieLab around = Mean(cells, self, wide.Of(i));

            double dl = self.L - around.L;
            double da = self.A - around.A;
            double db = self.B - around.B;

            double apart = Math.Sqrt((dl * dl) + (da * da) + (db * db));
            if (apart <= DeadBandDeltaE)
            {
                lifted[i] = self;
                continue;
            }

            // Soft threshold, then ceiling: the noise floor is subtracted rather than switched on
            // at, so a cell just past the dead band is lifted a little and not suddenly.
            double structure = Math.Min(MostLiftDeltaE / Amount, apart - DeadBandDeltaE);
            double scale = Amount * structure / apart;

            lifted[i] = new CieLab(self.L + (scale * dl), self.A + (scale * da), self.B + (scale * db));
        }

        return lifted;
    }

    /// <summary>Plain mean of a ring and the cell at its centre — distance only, colour ignored.</summary>
    private static CieLab Mean(ReadOnlySpan<CieLab> cells, CieLab self, ReadOnlySpan<int> ring)
    {
        double sumL = self.L, sumA = self.A, sumB = self.B;
        foreach (int j in ring)
        {
            sumL += cells[j].L;
            sumA += cells[j].A;
            sumB += cells[j].B;
        }

        double count = ring.Length + 1;
        return new CieLab(sumL / count, sumA / count, sumB / count);
    }
}
