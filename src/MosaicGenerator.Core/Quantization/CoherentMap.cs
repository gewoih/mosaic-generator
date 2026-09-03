using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Grid;

namespace MosaicGenerator.Core.Quantization;

/// <summary>
/// Nudges the nearest-shade choice made by <see cref="Quantizer"/> so a cell does not disagree
/// with its neighbours alone.
///
/// <see cref="ToneMap"/> spreads a photograph across the material's own lightness range, on
/// purpose — a sky flatter than one tonal step would otherwise sit under a single article. But the
/// palette is not a ladder ordered by lightness at fixed hue: a shift of a couple of units of L can
/// tip <see cref="Quantizer"/> onto a shade of visibly different saturation. <c>Quantizer</c> is not
/// wrong to do this — it picks the nearest colour for each cell independently, which is exactly its
/// job — but "independently" is precisely the gap: nothing tells it that twenty neighbours just
/// picked one shade and this cell is about to be the lone exception. In a flat sky that exception
/// reads as grime, not as an inaccuracy — a single wrong-toned piece is something the eye finds and
/// cannot unsee, unlike a merely imprecise shade sitting among matching neighbours.
///
/// This settles that after the fact: each cell picks the candidate that minimises its own colour
/// error <em>plus</em> a penalty for disagreeing with what nearby cells — weighted by how alike
/// their photograph colours are — chose. A hard edge (a beak against sky) has next to no weight
/// between its two sides, so the penalty vanishes there and the edge survives untouched; a smooth
/// gradient pays the penalty once, at whichever point crossing it is cheapest, so the transition
/// moves as a front rather than dissolving into scattered outliers.
///
/// The other half of the ledger — gradation the photograph held and the work laid as one shade — is
/// not addressed here, and cannot be by a rule of this shape. See <c>docs/kursy-i-tsvet-plan.md</c>,
/// step 1, for the three formulations that were measured against it and for why a penalty keyed on a
/// pair's own colour gap provably cannot tell the two losses apart.
/// </summary>
public static class CoherentMap
{
    /// <summary>
    /// Weight the neighbour-agreement term carries against a cell's own colour error. A rule of
    /// thumb, not a setting: high enough to erase a lone outlier, low enough that a flat sky does
    /// not collapse back onto a single article. See <c>docs/krap-tona-plan.md</c> for the sweep
    /// that picked it.
    /// </summary>
    private const double NeighbourWeight = 0.5;

    /// <summary>
    /// Passes over every cell. Diminishing returns after a handful: what one pass fixes, later
    /// passes mostly just confirm.
    /// </summary>
    private const int MaxPasses = 3;

    /// <summary>
    /// Re-settles <paramref name="initial"/> by iterated conditional modes: each cell, in a fixed
    /// order, takes whichever candidate minimises its own error plus the neighbour penalty against
    /// the neighbours' <em>current</em> choice, then the next cell sees that update. Fixed order and
    /// no randomness, so a run is exactly reproducible. Stops early once a full pass changes nothing.
    /// </summary>
    /// <param name="cellLab">One colour per cell, as sampled and stretched.</param>
    /// <param name="paletteLab">The shades, indexed as <paramref name="candidates"/> refers to them.</param>
    /// <param name="initial">The starting assignment — typically <see cref="Quantizer"/>'s own pick.</param>
    /// <param name="candidates">Which palette indices a cell may land on.</param>
    /// <param name="neighbourhood">Which cells count as near which, shared with the rest of the pipeline.</param>
    public static int[] Settle(
        ReadOnlySpan<CieLab> cellLab,
        ReadOnlySpan<CieLab> paletteLab,
        int[] initial,
        IReadOnlyList<int> candidates,
        CellNeighbourhood neighbourhood)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(neighbourhood);

        if (cellLab.Length != initial.Length)
        {
            throw new ArgumentException(
                $"Expected {cellLab.Length} indices, got {initial.Length}.", nameof(initial));
        }

        if (candidates.Count <= 1 || cellLab.Length == 0)
        {
            // Nothing to disagree about with one candidate, and nothing to settle with no cells.
            return initial;
        }

        var assigned = (int[])initial.Clone();

        // Neighbour weights depend only on the photograph colours, not on the current assignment,
        // so they are computed once and reused across every pass.
        var neighbourWeights = new double[cellLab.Length][];
        var neighbourCells = new int[cellLab.Length][];
        for (int i = 0; i < cellLab.Length; i++)
        {
            ReadOnlySpan<int> of = neighbourhood.Of(i);
            neighbourCells[i] = of.ToArray();
            var weights = new double[of.Length];
            for (int n = 0; n < of.Length; n++)
            {
                weights[n] = ColorDistance.NeighbourWeight(cellLab[i], cellLab[of[n]]);
            }

            neighbourWeights[i] = weights;
        }

        for (int pass = 0; pass < MaxPasses; pass++)
        {
            bool changed = false;

            for (int i = 0; i < assigned.Length; i++)
            {
                int[] neighbours = neighbourCells[i];
                double[] weights = neighbourWeights[i];
                double weightSum = 0.0;
                foreach (double w in weights)
                {
                    weightSum += w;
                }

                int best = assigned[i];
                double bestCost = double.MaxValue;

                foreach (int candidate in candidates)
                {
                    double cost = ColorDistance.MatchSquared(cellLab[i], paletteLab[candidate]);

                    if (weightSum > 0.0)
                    {
                        double disagreement = 0.0;
                        for (int n = 0; n < neighbours.Length; n++)
                        {
                            disagreement += weights[n] *
                                ColorDistance.MatchSquared(paletteLab[candidate], paletteLab[assigned[neighbours[n]]]);
                        }

                        cost += NeighbourWeight * (disagreement / weightSum);
                    }

                    // Ties keep the current choice when it is itself a tie, so a cell does not
                    // hop between equally good candidates from one pass to the next.
                    if (cost < bestCost || (cost == bestCost && candidate == assigned[i]))
                    {
                        bestCost = cost;
                        best = candidate;
                    }
                }

                if (best != assigned[i])
                {
                    assigned[i] = best;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        return assigned;
    }
}
