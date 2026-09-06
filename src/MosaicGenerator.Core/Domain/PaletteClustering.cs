using MosaicGenerator.Core.Colors;

namespace MosaicGenerator.Core.Domain;

/// <summary>
/// Groups the articles of a palette that are too close in colour to tell apart. Complete linkage:
/// a cluster is only formed when <em>every</em> pair inside it is within
/// <see cref="ThresholdDe"/> ΔE76, so a chain of near-neighbours does not drag a visibly different
/// shade in. Measured on ArtWorker 2026-09-06: 152 → 136 representatives, the largest group the six
/// whites. See <c>docs/redukciya-svyazka-plan.md</c> (TODO п. 15).
/// </summary>
internal static class PaletteClustering
{
    /// <summary>
    /// Plain CIE76, not the hue-weighted matching ΔE: the question here is whether two shades read
    /// as one physical colour, which does not depend on which matcher the bench happens to be
    /// running. Zashito, ne nastroyka (CLAUDE.md: инструмент для себя).
    /// </summary>
    internal const double ThresholdDe = 3.0;

    internal static IReadOnlyList<PaletteCluster> Compute(IReadOnlyList<PaletteColor> colors)
    {
        int n = colors.Count;

        // Pairwise ΔE once; n is ~150, so the full matrix is cheap and keeps the linkage loop simple.
        var d = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                double e = ColorDistance.CieDe76(colors[i].Lab, colors[j].Lab);
                d[i, j] = e;
                d[j, i] = e;
            }
        }

        // Each article starts alone; merge the closest two clusters by complete linkage until the
        // closest pair is beyond the threshold. Members kept ascending so ties are stable.
        var clusters = new List<List<int>>(n);
        for (int i = 0; i < n; i++)
        {
            clusters.Add([i]);
        }

        while (true)
        {
            int bestA = -1, bestB = -1;
            double best = double.MaxValue;

            for (int a = 0; a < clusters.Count; a++)
            {
                for (int b = a + 1; b < clusters.Count; b++)
                {
                    double linkage = 0.0;
                    foreach (int p in clusters[a])
                    {
                        foreach (int q in clusters[b])
                        {
                            linkage = Math.Max(linkage, d[p, q]);
                        }
                    }

                    if (linkage < best)
                    {
                        best = linkage;
                        bestA = a;
                        bestB = b;
                    }
                }
            }

            if (bestA < 0 || best >= ThresholdDe)
            {
                break;
            }

            clusters[bestA].AddRange(clusters[bestB]);
            clusters[bestA].Sort();
            clusters.RemoveAt(bestB);
        }

        // Representative: the medoid — least total ΔE to the other members — with the article name
        // as an ordinal tie-break so a rebuild picks the same one.
        var result = new List<PaletteCluster>(clusters.Count);
        foreach (List<int> members in clusters)
        {
            int representative = members[0];
            double bestCost = double.MaxValue;
            foreach (int candidate in members)
            {
                double cost = 0.0;
                foreach (int other in members)
                {
                    cost += d[candidate, other];
                }

                if (cost < bestCost
                    || (cost == bestCost
                        && string.CompareOrdinal(colors[candidate].Article, colors[representative].Article) < 0))
                {
                    bestCost = cost;
                    representative = candidate;
                }
            }

            result.Add(new PaletteCluster(representative, members));
        }

        // Ordered by representative index so RepresentativeIndices comes out ascending.
        result.Sort((x, y) => x.RepresentativeIndex.CompareTo(y.RepresentativeIndex));
        return result;
    }
}
