using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Rendering;

namespace MosaicGenerator.Core.Grid;

/// <summary>
/// Which tesserae sit near which, by centroid. Built once and shared: <see cref="Quantization.CellSmoother"/>,
/// <see cref="Quantization.PaletteReducer"/> and <see cref="Quantization.CoherentMap"/> all need the same
/// notion of "neighbour", and building three separate bucket grids over the same layout would be three
/// chances for the radius to drift apart.
/// </summary>
public sealed class CellNeighbourhood
{
    private readonly int[][] _neighbours;

    private CellNeighbourhood(int[][] neighbours)
    {
        _neighbours = neighbours;
    }

    /// <summary>
    /// One piece and its immediate ring: far enough to average a field, near enough that a
    /// gradient across the panel is untouched. Radius comes from the layout's own step rather than
    /// the spread of centroids, so it does not drift with however many cells the fill-in pass added.
    /// </summary>
    public static CellNeighbourhood Build(IReadOnlyList<Tessera> tesserae, MosaicLayout layout)
    {
        ArgumentNullException.ThrowIfNull(tesserae);
        ArgumentNullException.ThrowIfNull(layout);

        double reach = Math.Max(layout.StepXMm, layout.StepYMm) * 1.5;
        return Build(tesserae, reach);
    }

    /// <summary>Same neighbourhood, with the reach given directly rather than derived from a layout.</summary>
    public static CellNeighbourhood Build(IReadOnlyList<Tessera> tesserae, double reach)
    {
        ArgumentNullException.ThrowIfNull(tesserae);

        int n = tesserae.Count;
        var neighbours = new int[n][];
        if (n == 0 || reach <= 0.0)
        {
            for (int i = 0; i < n; i++)
            {
                neighbours[i] = [];
            }

            return new CellNeighbourhood(neighbours);
        }

        double reachSq = reach * reach;
        var buckets = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < n; i++)
        {
            (int, int) key = Bucket(tesserae[i].Centroid, reach);
            (buckets.TryGetValue(key, out List<int>? list) ? list : buckets[key] = []).Add(i);
        }

        var scratch = new List<int>(8);
        for (int i = 0; i < n; i++)
        {
            scratch.Clear();
            PointD ci = tesserae[i].Centroid;
            (int bx, int by) = Bucket(ci, reach);

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (!buckets.TryGetValue((bx + dx, by + dy), out List<int>? bucket))
                    {
                        continue;
                    }

                    foreach (int j in bucket)
                    {
                        if (j == i)
                        {
                            continue;
                        }

                        PointD cj = tesserae[j].Centroid;
                        double dxp = cj.X - ci.X;
                        double dyp = cj.Y - ci.Y;
                        if (((dxp * dxp) + (dyp * dyp)) <= reachSq)
                        {
                            scratch.Add(j);
                        }
                    }
                }
            }

            neighbours[i] = [.. scratch];
        }

        return new CellNeighbourhood(neighbours);
    }

    /// <summary>Neighbours of cell <paramref name="cell"/>, excluding itself.</summary>
    public ReadOnlySpan<int> Of(int cell) => _neighbours[cell];

    private static (int, int) Bucket(PointD p, double reach) =>
        ((int)Math.Floor(p.X / reach), (int)Math.Floor(p.Y / reach));
}
