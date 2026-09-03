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
    /// The palette indices still in use once the reduction stopped. What <see cref="CoherentMap"/>
    /// needs as its candidate set for the post-reduction settling pass — the orphan cells re-quantised
    /// mid-reduction went through <see cref="Quantizer.NearestIndex"/> one at a time, and can use a
    /// second look at what their neighbours settled on.
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
        IReadOnlyList<Tessera>? tesserae = null)
    {
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxColors, 1);

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
        if (colorsBefore <= maxColors)
        {
            return new ReductionOutcome
            {
                Indices = indices,
                ColorsBefore = colorsBefore,
                ColorsAfter = colorsBefore,
                ModulesReassigned = 0,
                RetainedColors = [.. cellsByColor.Keys],
            };
        }

        var reduced = (int[])indices.Clone();
        HashSet<int> retained = [.. cellsByColor.Keys];
        bool stoppedAtPinned = false;

        while (retained.Count > maxColors)
        {
            // Sorted so the sweep visits candidates in a fixed order and ties break the same way
            // on every run.
            int[] survivors = [.. retained.Order()];

            int victim = CheapestToDrop(cellLab, reduced, paletteLab, survivors, pinned, adjacency);
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

            foreach (int cell in orphans)
            {
                // Re-quantised from the cell's own colour rather than moved wholesale to one
                // replacement: cells that shared a discarded shade can legitimately land on
                // different survivors.
                int replacement = candidateIndices[Quantizer.NearestIndex(cellLab[cell], candidateLab)];

                reduced[cell] = replacement;
                cellsByColor[replacement].Add(cell);
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

        return new ReductionOutcome
        {
            Indices = reduced,
            ColorsBefore = colorsBefore,
            ColorsAfter = retained.Count,
            ModulesReassigned = reassigned,
            RetainedColors = [.. retained.Order()],
            StoppedAtPinnedColors = stoppedAtPinned,
        };
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
        CellNeighbourhood? adjacency)
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
