namespace MosaicGenerator.Core.Quantization;

/// <summary>
/// One rung of the reduction ladder: the layout once the greedy drop has walked down to
/// <see cref="ColorCount"/> shades. The greedy drop is monotone-nested — trimming to N and then
/// carrying on to N−1 lands on the same state as trimming straight to N−1 — so a single pass from
/// the shades in use down to the floor yields every rung at once.
/// </summary>
public sealed record ReductionRung
{
    /// <summary>Shades still standing at this rung.</summary>
    public required int ColorCount { get; init; }

    /// <summary>
    /// Perceptual cost of the drop that produced this rung: the summed displacement of the
    /// discarded shade's cells to their runners-up, scaled by how compact its footprint was.
    /// Zero for the top rung, where nothing has been dropped yet.
    /// </summary>
    public required double MarginalCost { get; init; }

    /// <summary>
    /// Modules on a different shade than the quantiser first chose, counted against the original
    /// mapping rather than accumulated per round.
    /// </summary>
    public required int ModulesReassigned { get; init; }

    /// <summary>The mapping at this rung — a private snapshot the caller may keep.</summary>
    public required int[] Indices { get; init; }

    /// <summary>Palette indices still in use at this rung — <see cref="CoherentMap"/>'s candidate set.</summary>
    public required IReadOnlyList<int> RetainedColors { get; init; }
}

/// <summary>Which rung <see cref="ReductionLadder.Knee"/> settled on, and the ratio that decided it.</summary>
public sealed record ColorLadderPick
{
    public required int Colors { get; init; }

    /// <summary>
    /// The expensive drop's <see cref="ReductionRung.MarginalCost"/> over the running median of the
    /// cheaper drops before it. When no knee was found this is the worst ratio the walk saw — below
    /// <see cref="ReductionLadder.KneeFactor"/> — and the pick is the ceiling.
    /// </summary>
    public required double KneeRatio { get; init; }
}

/// <summary>
/// The whole cost curve of the greedy reduction in one object: keep every shade, then drop the
/// cheapest, then the next cheapest, all the way to the floor, recording what each drop cost and
/// the mapping it left behind. Lets the right number of colours be chosen without a full
/// regeneration per trial.
/// </summary>
public sealed record ReductionLadder
{
    /// <summary>Rungs from the shades in use down to the floor, descending by <see cref="ReductionRung.ColorCount"/>.</summary>
    public required IReadOnlyList<ReductionRung> Rungs { get; init; }

    /// <summary>Shades the quantiser picked before any were dropped.</summary>
    public required int ColorsBefore { get; init; }

    /// <summary>Pinned articles alone reached the floor, so the walk stopped early.</summary>
    public bool StoppedAtPinnedColors { get; init; }

    /// <summary>
    /// How much dearer than the drops before it a drop has to be to read as the knee. Conservative
    /// on purpose: a lower factor stops sooner, keeps more colours and is the safer error.
    /// Calibrated later by a run over samples/ with the cartoons judged by eye — not in this task.
    /// </summary>
    public const double KneeFactor = 3.5;

    /// <summary>The deepest rung — the fewest shades the ladder was built down to.</summary>
    public ReductionRung Floor => Rungs[^1];

    /// <summary>The rung at exactly <paramref name="colors"/> shades, or the nearest one the ladder holds.</summary>
    public ReductionRung RungAt(int colors)
    {
        ReductionRung best = Rungs[0];
        foreach (ReductionRung rung in Rungs)
        {
            if (Math.Abs(rung.ColorCount - colors) < Math.Abs(best.ColorCount - colors))
            {
                best = rung;
            }
        }

        return best;
    }

    /// <summary>
    /// How many shades to keep. Walks the ladder from many colours down — the cheap drops
    /// (background) first — carrying the median <see cref="ReductionRung.MarginalCost"/> of the
    /// drops already passed. The knee is the first drop dearer than <see cref="KneeFactor"/> times
    /// that median, once at least three drops have been seen; the pick is the colour count just
    /// above it. An absolute cost threshold would be a fit to one panel — the marginal cost per
    /// further shade dropped is the self-calibrating signal. A smooth curve (no knee) returns
    /// <paramref name="ceiling"/>: a spare colour beats a lost subject.
    /// </summary>
    public ColorLadderPick Knee(int ceiling)
    {
        int hi = Math.Max(1, Math.Min(ceiling, ColorsBefore));
        int lo = Math.Min(Math.Max(4, Floor.ColorCount), hi);

        var seen = new List<double>();
        double worst = 0.0;

        // Rungs[0] is the top rung, cost 0 — skip it; each later rung carries the cost of its drop.
        for (int i = 1; i < Rungs.Count; i++)
        {
            double cost = Rungs[i].MarginalCost;

            if (seen.Count >= 3)
            {
                double median = Median(seen);
                double ratio = median > 0.0 ? cost / median : 0.0;
                worst = Math.Max(worst, ratio);

                if (ratio > KneeFactor)
                {
                    return new ColorLadderPick
                    {
                        Colors = Math.Clamp(Rungs[i - 1].ColorCount, lo, hi),
                        KneeRatio = ratio,
                    };
                }
            }

            seen.Add(cost);
        }

        return new ColorLadderPick { Colors = Math.Clamp(ceiling, lo, hi), KneeRatio = worst };
    }

    private static double Median(List<double> values)
    {
        double[] sorted = [.. values];
        Array.Sort(sorted);
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }
}
