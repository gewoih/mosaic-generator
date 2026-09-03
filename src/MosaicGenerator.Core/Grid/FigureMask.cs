using MosaicGenerator.Core.Colors;

namespace MosaicGenerator.Core.Grid;

/// <summary>
/// Foreground / background over the direction-field grid: 1 where the cell belongs to the subject,
/// 0 where it belongs to the surround. The surround is learned from the colour of the frame's
/// perimeter band and flood-filled inward; whatever the flood does not reach is the figure.
///
/// This exists so <see cref="ContourSet"/> can draw the silhouette as the boundary of one connected
/// region — a closed ring — instead of a level set of edge strength that breaks wherever the subject
/// happens to match the surround in tone. The break the level set cannot cross, this can, as long as
/// the subject differs from the surround in <em>some</em> Lab channel (the grey wing against the
/// grey-blue sky).
///
/// Returns <c>null</c> when there is no clean figure to find — a busy or gradated surround, or a
/// result that fills almost the whole frame or almost none of it. Callers fall back to the level set.
/// </summary>
internal sealed class FigureMask
{
    private readonly double[] _cells;

    private FigureMask(int width, int height, double[] cells)
    {
        Width = width;
        Height = height;
        _cells = cells;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Row-major grid, 1.0 on the figure and 0.0 on the surround — for marching squares at level 0.5.</summary>
    public ReadOnlySpan<double> Cells => _cells;

    /// <summary>True where field-normalised (<paramref name="u"/>, <paramref name="v"/>) falls on the figure.</summary>
    public bool ForegroundAt(double u, double v)
    {
        int x = Math.Clamp((int)Math.Round(Math.Clamp(u, 0.0, 1.0) * (Width - 1)), 0, Width - 1);
        int y = Math.Clamp((int)Math.Round(Math.Clamp(v, 0.0, 1.0) * (Height - 1)), 0, Height - 1);
        return _cells[(y * Width) + x] > 0.5;
    }

    /// <param name="edgeLevel">
    /// The edge strength that counts as being on a form (<see cref="ContourSet.LevelFor"/>). A cell
    /// this strong is a wall the surround flood may not pass, so a thin bright rim keeps the flood out
    /// of the figure even before the colour test has decided.
    /// </param>
    public static FigureMask? Build(
        int width, int height, ReadOnlySpan<CieLab> colour, ReadOnlySpan<double> edge, double edgeLevel)
    {
        if (colour.Length != width * height || edge.Length != width * height)
        {
            return null;
        }

        int band = Math.Max(2, (int)Math.Round(0.06 * Math.Min(width, height)));

        // Colour of the perimeter band, from which the surround model is learned.
        var borderColours = new List<CieLab>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (x < band || x >= width - band || y < band || y >= height - band)
                {
                    borderColours.Add(colour[(y * width) + x]);
                }
            }
        }

        if (borderColours.Count < 16)
        {
            return null;
        }

        IReadOnlyList<CieLab> clusters = ClusterBorder(borderColours);

        // Spread of the band around its own clusters. A uniform wall runs a couple of ΔE; a gradated
        // sky or a busy street runs far more, and there the "close to the border" test would swallow
        // the subject too. Bail rather than hand back a mask that is mostly holes.
        double meanSpread = 0.0;
        foreach (CieLab c in borderColours)
        {
            meanSpread += NearestDistance(c, clusters);
        }

        meanSpread /= borderColours.Count;
        if (meanSpread > 12.0)
        {
            return null;
        }

        double tau = Math.Clamp(3.0 * meanSpread, 6.0, 16.0);
        double wall = edgeLevel > 0.0 ? edgeLevel : double.PositiveInfinity;

        // 0 = surround by colour, 1 = figure by colour, 2 = wall (strong edge, blocks the flood).
        var kind = new byte[width * height];
        for (int i = 0; i < kind.Length; i++)
        {
            bool nearSurround = NearestDistance(colour[i], clusters) < tau;
            kind[i] = nearSurround ? (byte)0 : (edge[i] >= wall ? (byte)2 : (byte)1);
        }

        var foreground = FloodedFigure(kind, width, height);

        Close(foreground, width, height, CloseRadius(width, height));
        KeepLargestComponent(foreground, width, height);
        FillHoles(foreground, width, height);

        int fg = 0;
        foreach (bool b in foreground)
        {
            if (b)
            {
                fg++;
            }
        }

        double fraction = (double)fg / foreground.Length;
        if (fraction is < 0.03 or > 0.85)
        {
            return null;
        }

        if (TouchesEveryEdge(foreground, width, height))
        {
            return null;
        }

        var cells = new double[width * height];
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i] = foreground[i] ? 1.0 : 0.0;
        }

        return new FigureMask(width, height, cells);
    }

    private static int CloseRadius(int width, int height) =>
        // About one module: the field runs at roughly six cells per module.
        (int)Math.Clamp(Math.Min(width, height) / 64.0, 2.0, 8.0);

    /// <summary>Up to three colour clusters over the border band; nearby clusters merged.</summary>
    private static IReadOnlyList<CieLab> ClusterBorder(IReadOnlyList<CieLab> samples)
    {
        // Deterministic k-means++ seeding: the mean, then the two points farthest from what is chosen.
        CieLab mean = Mean(samples);
        var centres = new List<CieLab> { mean };
        for (int k = 1; k < 3; k++)
        {
            CieLab farthest = samples[0];
            double best = -1.0;
            foreach (CieLab s in samples)
            {
                double d = NearestDistance(s, centres);
                if (d > best)
                {
                    best = d;
                    farthest = s;
                }
            }

            centres.Add(farthest);
        }

        for (int iter = 0; iter < 12; iter++)
        {
            var sum = new (double L, double A, double B, int N)[centres.Count];
            foreach (CieLab s in samples)
            {
                int c = NearestIndex(s, centres);
                sum[c] = (sum[c].L + s.L, sum[c].A + s.A, sum[c].B + s.B, sum[c].N + 1);
            }

            for (int c = 0; c < centres.Count; c++)
            {
                if (sum[c].N > 0)
                {
                    centres[c] = new CieLab(sum[c].L / sum[c].N, sum[c].A / sum[c].N, sum[c].B / sum[c].N);
                }
            }
        }

        // Merge centres closer than the neighbour falloff — they describe one wall, not two.
        var merged = new List<CieLab>();
        foreach (CieLab c in centres)
        {
            if (merged.TrueForAll(m => ColorDistance.CieDe76(m, c) > 6.0))
            {
                merged.Add(c);
            }
        }

        return merged;
    }

    private static CieLab Mean(IReadOnlyList<CieLab> xs)
    {
        double l = 0.0, a = 0.0, b = 0.0;
        foreach (CieLab x in xs)
        {
            l += x.L;
            a += x.A;
            b += x.B;
        }

        return new CieLab(l / xs.Count, a / xs.Count, b / xs.Count);
    }

    private static int NearestIndex(CieLab c, IReadOnlyList<CieLab> centres)
    {
        int best = 0;
        double bestD = double.MaxValue;
        for (int i = 0; i < centres.Count; i++)
        {
            double d = ColorDistance.CieDe76Squared(c, centres[i]);
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }

        return best;
    }

    private static double NearestDistance(CieLab c, IReadOnlyList<CieLab> centres)
    {
        double best = double.MaxValue;
        foreach (CieLab centre in centres)
        {
            best = Math.Min(best, ColorDistance.CieDe76Squared(c, centre));
        }

        return Math.Sqrt(best);
    }

    /// <summary>Flood the surround inward from the frame, then the figure is whatever it did not reach.</summary>
    private static bool[] FloodedFigure(byte[] kind, int width, int height)
    {
        var outside = new bool[kind.Length];
        var stack = new Stack<int>();

        void Seed(int k)
        {
            if (!outside[k] && kind[k] == 0)
            {
                outside[k] = true;
                stack.Push(k);
            }
        }

        for (int x = 0; x < width; x++)
        {
            Seed(x);
            Seed(((height - 1) * width) + x);
        }

        for (int y = 0; y < height; y++)
        {
            Seed(y * width);
            Seed((y * width) + width - 1);
        }

        while (stack.Count > 0)
        {
            int k = stack.Pop();
            int x = k % width;
            int y = k / width;
            if (x > 0) Seed(k - 1);
            if (x < width - 1) Seed(k + 1);
            if (y > 0) Seed(k - width);
            if (y < height - 1) Seed(k + width);
        }

        var figure = new bool[kind.Length];
        for (int i = 0; i < figure.Length; i++)
        {
            figure[i] = !outside[i];
        }

        return figure;
    }

    /// <summary>Dilate then erode by <paramref name="radius"/> — bridges a gap the colour test leaves open.</summary>
    private static void Close(bool[] mask, int width, int height, int radius)
    {
        Morph(mask, width, height, radius, dilate: true);
        Morph(mask, width, height, radius, dilate: false);
    }

    private static void Morph(bool[] mask, int width, int height, int radius, bool dilate)
    {
        var scratch = new bool[mask.Length];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool hit = !dilate;
                for (int dx = -radius; dx <= radius && hit == !dilate; dx++)
                {
                    int xx = Math.Clamp(x + dx, 0, width - 1);
                    if (mask[(y * width) + xx] == dilate)
                    {
                        hit = dilate;
                    }
                }

                scratch[(y * width) + x] = hit;
            }
        }

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                bool hit = !dilate;
                for (int dy = -radius; dy <= radius && hit == !dilate; dy++)
                {
                    int yy = Math.Clamp(y + dy, 0, height - 1);
                    if (scratch[(yy * width) + x] == dilate)
                    {
                        hit = dilate;
                    }
                }

                mask[(y * width) + x] = hit;
            }
        }
    }

    private static void KeepLargestComponent(bool[] mask, int width, int height)
    {
        var seen = new bool[mask.Length];
        var best = new List<int>();
        var current = new List<int>();
        var stack = new Stack<int>();

        for (int start = 0; start < mask.Length; start++)
        {
            if (!mask[start] || seen[start])
            {
                continue;
            }

            current.Clear();
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int k = stack.Pop();
                current.Add(k);
                int x = k % width;
                int y = k / width;
                void Visit(int n)
                {
                    if (mask[n] && !seen[n])
                    {
                        seen[n] = true;
                        stack.Push(n);
                    }
                }

                if (x > 0) Visit(k - 1);
                if (x < width - 1) Visit(k + 1);
                if (y > 0) Visit(k - width);
                if (y < height - 1) Visit(k + width);
            }

            if (current.Count > best.Count)
            {
                best = [.. current];
            }
        }

        Array.Clear(mask);
        foreach (int k in best)
        {
            mask[k] = true;
        }
    }

    /// <summary>Anything enclosed by the figure — an eye, a gap between wings — is filled in.</summary>
    private static void FillHoles(bool[] mask, int width, int height)
    {
        var kind = new byte[mask.Length];
        for (int i = 0; i < kind.Length; i++)
        {
            kind[i] = mask[i] ? (byte)1 : (byte)0;
        }

        bool[] figure = FloodedFigure(kind, width, height);
        Array.Copy(figure, mask, mask.Length);
    }

    private static bool TouchesEveryEdge(bool[] mask, int width, int height)
    {
        bool top = false, bottom = false, left = false, right = false;
        for (int x = 0; x < width; x++)
        {
            top |= mask[x];
            bottom |= mask[((height - 1) * width) + x];
        }

        for (int y = 0; y < height; y++)
        {
            left |= mask[y * width];
            right |= mask[(y * width) + width - 1];
        }

        return top && bottom && left && right;
    }
}
