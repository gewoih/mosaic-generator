using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Grid;
using MosaicGenerator.Core.Rendering;

namespace MosaicGenerator.Core.Quantization;

public sealed record ReductionOutcome
{
    public required int[] Indices { get; init; }

    public required int ColorsBefore { get; init; }

    public required int ColorsAfter { get; init; }

    public required int ModulesReassigned { get; init; }

    /// <summary>
    /// The palette indices still in use once the reduction stopped — <see cref="CoherentMap"/>'s
    /// candidate set for the settling pass that follows the reduction.
    /// </summary>
    public required IReadOnlyList<int> RetainedColors { get; init; }

    /// <summary>
    /// True when pinned articles alone already exceed the ceiling, so the ceiling gave way.
    /// A pin is an explicit instruction; the ceiling is a rule of thumb.
    /// </summary>
    public bool StoppedAtPinnedColors { get; init; }
}

/// <summary>
/// Trims the layout down to a workable number of shades. Quantising a photograph against a large
/// palette leaves a long tail of colours used once or twice across the whole panel: they add no
/// legible detail, but every one of them is a separate article to order.
/// </summary>
public static class PaletteReducer
{
    public static ReductionOutcome Reduce(
        ReadOnlySpan<LinearRgb> cells, int[] indices, Palette palette, int maxColors)
    {
        ArgumentNullException.ThrowIfNull(palette);

        return Reduce(Quantizer.ToLab(cells), indices, PaletteObservation.Lab(palette), maxColors, null);
    }

    /// <summary>
    /// Drops shades one at a time, cheapest first, re-quantising the cells of each casualty as it
    /// goes.
    ///
    /// Cheapest is measured in colour error, not in how rare the shade is. Rarity is the wrong
    /// question: a beak, a catchlight or a red accent can be forty tesserae out of seven thousand
    /// and still be the thing the picture is about, and dropping by count kills those first while
    /// keeping a shade that nobody could tell from its neighbour. What actually costs nothing to
    /// lose is a shade whose cells have somewhere close to go.
    /// </summary>
    public static ReductionOutcome Reduce(
        ReadOnlySpan<CieLab> cellLab,
        int[] indices,
        ReadOnlySpan<CieLab> paletteLab,
        int maxColors,
        IReadOnlySet<int>? pinned,
        IReadOnlyList<Tessera>? tesserae = null,
        CellNeighbourhood? neighbourhood = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxColors, 1);

        ReductionLadder ladder = BuildLadder(
            cellLab, indices, paletteLab, maxColors, pinned, tesserae, neighbourhood);
        ReductionRung stop = ladder.Rungs[^1];

        return new ReductionOutcome
        {
            Indices = stop.Indices,
            ColorsBefore = ladder.ColorsBefore,
            ColorsAfter = stop.ColorCount,
            ModulesReassigned = stop.ModulesReassigned,
            RetainedColors = stop.RetainedColors,
            StoppedAtPinnedColors = ladder.StoppedAtPinnedColors,
        };
    }

    /// <summary>
    /// The whole cost curve in one nested pass: keep every shade, then drop the cheapest, then the
    /// next, down to <paramref name="floor"/>, recording after each drop what it cost and the
    /// mapping it left. The greedy drop is monotone-nested, so this is exactly what a stack of
    /// <see cref="Reduce"/> calls would produce, at the price of one.
    /// </summary>
    public static ReductionLadder BuildLadder(
        ReadOnlySpan<CieLab> cellLab,
        int[] indices,
        ReadOnlySpan<CieLab> paletteLab,
        int floor,
        IReadOnlySet<int>? pinned,
        IReadOnlyList<Tessera>? tesserae = null,
        CellNeighbourhood? neighbourhood = null)
    {
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentOutOfRangeException.ThrowIfLessThan(floor, 1);

        // Adjacency between cells, so a shade gathered into a compact blob — an eye, a catchlight —
        // costs more to lose than the same tessera count scattered across the panel.
        CellNeighbourhood? adjacency = tesserae is null ? null : BuildAdjacency(tesserae);

        if (cellLab.Length != indices.Length)
        {
            throw new ArgumentException(
                $"Expected {cellLab.Length} indices, got {indices.Length}.", nameof(indices));
        }

        var cellsByColor = new Dictionary<int, List<int>>();
        for (int cell = 0; cell < indices.Length; cell++)
        {
            if (!cellsByColor.TryGetValue(indices[cell], out List<int>? bucket))
            {
                bucket = [];
                cellsByColor[indices[cell]] = bucket;
            }

            bucket.Add(cell);
        }

        int colorsBefore = cellsByColor.Count;
        var rungs = new List<ReductionRung>();

        if (colorsBefore <= floor)
        {
            rungs.Add(new ReductionRung
            {
                ColorCount = colorsBefore,
                MarginalCost = 0.0,
                ModulesReassigned = 0,
                Indices = indices,
                RetainedColors = [.. cellsByColor.Keys],
            });

            return new ReductionLadder { Rungs = rungs, ColorsBefore = colorsBefore };
        }

        var reduced = (int[])indices.Clone();
        HashSet<int> retained = [.. cellsByColor.Keys];
        bool stoppedAtPinned = false;

        rungs.Add(new ReductionRung
        {
            ColorCount = retained.Count,
            MarginalCost = 0.0,
            ModulesReassigned = 0,
            Indices = (int[])reduced.Clone(),
            RetainedColors = [.. retained.Order()],
        });

        while (retained.Count > floor)
        {
            // Sorted so the sweep visits candidates in a fixed order and ties break the same way
            // on every run.
            int[] survivors = [.. retained.Order()];

            int victim = CheapestToDrop(
                cellLab, reduced, paletteLab, survivors, pinned, adjacency, out double victimCost);
            if (victim < 0)
            {
                // Everything still standing is pinned.
                stoppedAtPinned = true;
                break;
            }

            retained.Remove(victim);
            List<int> orphans = cellsByColor[victim];
            cellsByColor.Remove(victim);

            int[] candidateIndices = [.. retained.Order()];
            var candidateLab = new CieLab[candidateIndices.Length];
            for (int i = 0; i < candidateIndices.Length; i++)
            {
                candidateLab[i] = paletteLab[candidateIndices[i]];
            }

            // Re-quantised from each cell's own colour rather than moved wholesale to one
            // replacement: cells that shared a discarded shade can legitimately land on different
            // survivors. When the caller shares a neighbourhood, the pick also weighs what nearby
            // cells settled on — the point-by-point NearestIndex is exactly where the first
            // CoherentMap pass's work was being undone (TODO п. 13, docs/redukciya-svyazka-plan.md).
            if (neighbourhood is null)
            {
                foreach (int cell in orphans)
                {
                    int replacement = candidateIndices[Quantizer.NearestIndex(cellLab[cell], candidateLab)];
                    reduced[cell] = replacement;
                    cellsByColor[replacement].Add(cell);
                }
            }
            else
            {
                HandOutOrphans(orphans, reduced, cellLab, paletteLab, candidateIndices, neighbourhood);
                foreach (int cell in orphans)
                {
                    cellsByColor[reduced[cell]].Add(cell);
                }
            }

            // Counted against the original mapping rather than accumulated per round: a cell whose
            // replacement is itself discarded later moves twice, but it is still one module that
            // ended up somewhere other than where it started.
            int reassigned = 0;
            for (int cell = 0; cell < reduced.Length; cell++)
            {
                if (reduced[cell] != indices[cell])
                {
                    reassigned++;
                }
            }

            rungs.Add(new ReductionRung
            {
                ColorCount = retained.Count,
                MarginalCost = victimCost,
                ModulesReassigned = reassigned,
                Indices = (int[])reduced.Clone(),
                RetainedColors = [.. retained.Order()],
            });
        }

        return new ReductionLadder
        {
            Rungs = rungs,
            ColorsBefore = colorsBefore,
            StoppedAtPinnedColors = stoppedAtPinned,
        };
    }

    /// <summary>
    /// Hands the cells of a just-dropped shade to survivors with an eye on what their neighbours
    /// carry, rather than one nearest-shade lookup each. Seeds every orphan from its own colour,
    /// then relaxes once — orphans with the most already-settled neighbours first, so a confident
    /// context is spent before a doubtful one — choosing the candidate that minimises the cell's
    /// own colour error plus <see cref="CoherentMap.NeighbourWeight"/> times its weighted
    /// disagreement with the neighbours' current pick. Same cost shape and same <c>Match</c> as
    /// <see cref="CoherentMap"/>; this only stops the reducer from recreating the very
    /// disagreement the first settling pass removed.
    /// </summary>
    private static void HandOutOrphans(
        List<int> orphans,
        int[] assigned,
        ReadOnlySpan<CieLab> cellLab,
        ReadOnlySpan<CieLab> paletteLab,
        int[] candidates,
        CellNeighbourhood neighbourhood)
    {
        var candidateLab = new CieLab[candidates.Length];
        for (int i = 0; i < candidates.Length; i++)
        {
            candidateLab[i] = paletteLab[candidates[i]];
        }

        // Seed: nearest survivor to the cell's own colour — the same starting point the plain
        // hand-out would have reached.
        var orphanSet = new HashSet<int>(orphans);
        foreach (int cell in orphans)
        {
            assigned[cell] = candidates[Quantizer.NearestIndex(cellLab[cell], candidateLab)];
        }

        // Settle the well-surrounded orphans first; ties on the cell index, so a run repeats.
        int[] order = [.. orphans];
        int SettledNeighbours(int cell)
        {
            int count = 0;
            foreach (int j in neighbourhood.Of(cell))
            {
                if (!orphanSet.Contains(j))
                {
                    count++;
                }
            }

            return count;
        }

        Array.Sort(order, (a, b) =>
        {
            int byContext = SettledNeighbours(b).CompareTo(SettledNeighbours(a));
            return byContext != 0 ? byContext : a.CompareTo(b);
        });

        foreach (int cell in order)
        {
            ReadOnlySpan<int> ring = neighbourhood.Of(cell);
            double weightSum = 0.0;
            foreach (int j in ring)
            {
                weightSum += ColorDistance.NeighbourWeight(cellLab[cell], cellLab[j]);
            }

            int best = assigned[cell];
            double bestCost = double.MaxValue;
            foreach (int candidate in candidates)
            {
                double cost = ColorDistance.MatchSquared(cellLab[cell], paletteLab[candidate]);
                if (weightSum > 0.0)
                {
                    double disagreement = 0.0;
                    foreach (int j in ring)
                    {
                        disagreement += ColorDistance.NeighbourWeight(cellLab[cell], cellLab[j])
                            * ColorDistance.MatchSquared(paletteLab[candidate], paletteLab[assigned[j]]);
                    }

                    cost += CoherentMap.NeighbourWeight * (disagreement / weightSum);
                }

                if (cost < bestCost || (cost == bestCost && candidate == assigned[cell]))
                {
                    bestCost = cost;
                    best = candidate;
                }
            }

            assigned[cell] = best;
        }
    }

    /// <summary>
    /// Shade whose loss would displace its cells least, summed over every cell wearing it.
    ///
    /// Costed for all candidates in one sweep of the layout. A cell's own shade is the nearest
    /// survivor it currently has, so what its shade's removal would cost that cell is its distance
    /// to the runner-up — which is the same inner loop for every candidate at once, rather than
    /// one pass of the layout per candidate.
    ///
    /// Returns -1 when nothing may be dropped.
    /// </summary>
    private static int CheapestToDrop(
        ReadOnlySpan<CieLab> cellLab,
        int[] assigned,
        ReadOnlySpan<CieLab> paletteLab,
        int[] survivors,
        IReadOnlySet<int>? pinned,
        CellNeighbourhood? adjacency,
        out double cheapestCost)
    {
        var cost = new Dictionary<int, double>(survivors.Length);
        foreach (int color in survivors)
        {
            cost[color] = 0.0;
        }

        for (int cell = 0; cell < assigned.Length; cell++)
        {
            int own = assigned[cell];
            CieLab target = cellLab[cell];

            double nearestOther = double.MaxValue;
            foreach (int color in survivors)
            {
                if (color == own)
                {
                    continue;
                }

                double distance = ColorDistance.Match(target, paletteLab[color]);
                if (distance < nearestOther)
                {
                    nearestOther = distance;
                }
            }

            // Summed as plain dE rather than squared: this is total perceptual displacement, and
            // squaring it would let one badly stranded cell outvote a hundred mildly moved ones.
            cost[own] += nearestOther;
        }

        if (adjacency is not null)
        {
            // A shade whose cells hang together in one blob is load-bearing — an eye, a beak, a
            // catchlight — so raise its cost by how compact it is. Scattered cells barely move.
            foreach (int color in survivors)
            {
                double compactness = LargestComponentFraction(assigned, color, adjacency);
                cost[color] *= 1.0 + (1.6 * compactness);
            }
        }

        int victim = -1;
        double cheapest = double.MaxValue;

        foreach (int color in survivors)
        {
            if (pinned is not null && pinned.Contains(color))
            {
                continue;
            }

            // Ties break on the higher palette index, so a run is reproducible.
            if (cost[color] < cheapest || (cost[color] == cheapest && color > victim))
            {
                cheapest = cost[color];
                victim = color;
            }
        }

        cheapestCost = victim >= 0 ? cheapest : 0.0;
        return victim;
    }

    /// <summary>
    /// Neighbours of every cell, by tessera centroid, so a shade gathered into a compact blob costs
    /// more to lose than the same tessera count scattered across the panel. Radius estimated from
    /// the centroid spread and the count, since this overload has no layout to take a step from —
    /// callers with a layout should build a <see cref="CellNeighbourhood"/> themselves and share it.
    /// </summary>
    private static CellNeighbourhood BuildAdjacency(IReadOnlyList<Tessera> tesserae)
    {
        int n = tesserae.Count;

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (int i = 0; i < n; i++)
        {
            PointD c = tesserae[i].Centroid;
            minX = Math.Min(minX, c.X);
            minY = Math.Min(minY, c.Y);
            maxX = Math.Max(maxX, c.X);
            maxY = Math.Max(maxY, c.Y);
        }

        double extentX = maxX - minX;
        double extentY = maxY - minY;
        double span = Math.Max(extentX, extentY);

        // sqrt(area / n) is the spacing of a roughly square grid; the second term catches a nearly
        // one-dimensional layout, where the first collapses to zero.
        double spacing = Math.Max(
            Math.Sqrt(Math.Max(1e-9, extentX * extentY) / Math.Max(1, n)),
            span / Math.Max(1, n - 1));
        spacing = Math.Max(1e-3, spacing);

        return CellNeighbourhood.Build(tesserae, spacing * 1.5);
    }

    /// <summary>
    /// How load-bearing a shade's footprint is: the fraction of its cells in its largest connected
    /// patch, scaled down for shades too small to carry a feature at all. 0 (scattered or trivial)
    /// to 1 (a solid blob of real size).
    /// </summary>
    private static double LargestComponentFraction(int[] assigned, int color, CellNeighbourhood adjacency)
    {
        var members = new List<int>();
        for (int cell = 0; cell < assigned.Length; cell++)
        {
            if (assigned[cell] == color)
            {
                members.Add(cell);
            }
        }

        if (members.Count <= 1)
        {
            return 0.0;
        }

        double sizeFactor = Math.Min(1.0, members.Count / 6.0);

        var parent = new Dictionary<int, int>(members.Count);
        foreach (int m in members)
        {
            parent[m] = m;
        }

        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }

            return x;
        }

        foreach (int m in members)
        {
            foreach (int neighbour in adjacency.Of(m))
            {
                if (parent.ContainsKey(neighbour))
                {
                    parent[Find(neighbour)] = Find(m);
                }
            }
        }

        var sizes = new Dictionary<int, int>();
        int largest = 0;
        foreach (int m in members)
        {
            int root = Find(m);
            int size = sizes.GetValueOrDefault(root) + 1;
            sizes[root] = size;
            largest = Math.Max(largest, size);
        }

        return (double)largest / members.Count * sizeFactor;
    }
}
