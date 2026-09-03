using MosaicGenerator.Core.Rendering;

namespace MosaicGenerator.Core.Grid;

/// <summary>
/// The lines a mosaicist would draw first: the subject's silhouette and its major internal
/// boundaries. The silhouette is the boundary of the connected figure region (<see cref="FigureMask"/>)
/// when there is one — a closed ring, contour 0 — and the internal boundaries are the level set of
/// the edge-strength field at a high percentile. Both are traced with marching squares, chained into
/// polylines and simplified. Everything downstream — the contour courses (opus vermiculatum) and the
/// distance field that steers the background (opus musivum) — is built from these.
/// </summary>
public static class ContourSet
{
    /// <summary>Contour polylines in field millimetres. Empty for a photograph with no real edges.</summary>
    public static IReadOnlyList<PointD[]> Extract(
        DirectionField field, double fieldWidthMm, double fieldHeightMm, double moduleMm)
    {
        ArgumentNullException.ThrowIfNull(field);

        int w = field.Width;
        int h = field.Height;
        ReadOnlySpan<double> edge = field.EdgeCells;

        double sx = fieldWidthMm / Math.Max(1, w - 1);
        double sy = fieldHeightMm / Math.Max(1, h - 1);
        double weld = moduleMm * 0.7;
        double minLength = moduleMm * 4.0;
        double epsilon = moduleMm * 0.75;

        List<PointD[]> ScaleAndSimplify(List<(PointD A, PointD B)> segs, bool exactChain = false)
        {
            for (int i = 0; i < segs.Count; i++)
            {
                (PointD a, PointD b) = segs[i];
                segs[i] = (new PointD(a.X * sx, a.Y * sy), new PointD(b.X * sx, b.Y * sy));
            }

            var lines = new List<PointD[]>();
            IEnumerable<List<PointD>> chains = exactChain ? ChainExact(segs) : Chain(segs, weld);
            foreach (List<PointD> chain in chains)
            {
                PointD[] simplified = DouglasPeucker(chain, epsilon);
                if (Length(simplified) >= minLength)
                {
                    lines.Add(simplified);
                }
            }

            return lines;
        }

        // The silhouette: the boundary of the connected figure region, a closed ring even where the
        // subject matches the surround in tone and the edge level set below would break. Its internal
        // boundaries still come from the level set.
        var rings = new List<PointD[]>();
        FigureMask? figure = field.Figure;
        if (figure is not null)
        {
            rings.AddRange(ScaleAndSimplify(MarchingSquares(figure.Cells, w, h, 0.5), exactChain: true));
        }

        var polylines = new List<PointD[]>(rings);

        // Relative to how strong the strongest edges are, not to a fixed percentile: a thin
        // silhouette is only a percent or two of the pixels, so a percentile threshold would sit
        // in the flat background and find nothing.
        double level = LevelFor(edge);
        if (level > 0.0)
        {
            foreach (PointD[] line in ScaleAndSimplify(MarchingSquares(edge, w, h, level)))
            {
                // Drop what only retraces a silhouette ring we already have from the mask.
                if (rings.Exists(r => Overlaps(line, r, weld)))
                {
                    continue;
                }

                polylines.Add(line);
            }
        }

        if (polylines.Count == 0)
        {
            return [];
        }

        // Longest first, as CourseGuidance and Tessellation both assume. The silhouette rings are
        // kept ahead of the level-set contours regardless of length: they are the structural line
        // the fill echoes, and the level set only ever holds internal detail once a ring is present.
        int Rank(PointD[] p) => rings.Contains(p) ? 0 : 1;
        polylines.Sort((p, q) =>
        {
            int byRing = Rank(p).CompareTo(Rank(q));
            if (byRing != 0)
            {
                return byRing;
            }

            int byLen = Length(q).CompareTo(Length(p));
            if (byLen != 0)
            {
                return byLen;
            }

            int byY = p[0].Y.CompareTo(q[0].Y);
            return byY != 0 ? byY : p[0].X.CompareTo(q[0].X);
        });

        return polylines;
    }

    /// <summary>
    /// The edge strength that counts as being on a contour — a high percentile of the field, floored
    /// so a soft photograph still yields a line. Zero when the photograph has no real edges. Shared
    /// by <see cref="Extract"/>, <see cref="FigureMask"/> and the piece-size signal in Tessellation
    /// so all three agree on where a form is.
    /// </summary>
    internal static double LevelFor(ReadOnlySpan<double> edge)
    {
        double strong = Percentile(edge, 0.99);
        return strong < 0.15 ? 0.0 : Math.Max(0.12, strong * 0.42);
    }

    /// <summary>True when <paramref name="line"/> runs within <paramref name="tol"/> of <paramref name="other"/> for most of its length.</summary>
    private static bool Overlaps(PointD[] line, PointD[] other, double tol)
    {
        int near = 0;
        foreach (PointD p in line)
        {
            if (DistanceToPolyline(p, other) <= tol)
            {
                near++;
            }
        }

        return near >= line.Length * 0.6;
    }

    private static double DistanceToPolyline(PointD p, PointD[] poly)
    {
        double best = double.MaxValue;
        for (int i = 1; i < poly.Length; i++)
        {
            best = Math.Min(best, PerpendicularDistance(p, poly[i - 1], poly[i]));
        }

        return best;
    }

    private static double Percentile(ReadOnlySpan<double> values, double fraction)
    {
        var sorted = values.ToArray();
        Array.Sort(sorted);
        int index = Math.Clamp((int)(fraction * (sorted.Length - 1)), 0, sorted.Length - 1);
        return sorted[index];
    }

    private static List<(PointD A, PointD B)> MarchingSquares(
        ReadOnlySpan<double> f, int w, int h, double level)
    {
        var segments = new List<(PointD, PointD)>();

        for (int y = 0; y < h - 1; y++)
        {
            for (int x = 0; x < w - 1; x++)
            {
                double tl = f[(y * w) + x];
                double tr = f[(y * w) + x + 1];
                double br = f[((y + 1) * w) + x + 1];
                double bl = f[((y + 1) * w) + x];

                int code = 0;
                if (tl > level) code |= 1;
                if (tr > level) code |= 2;
                if (br > level) code |= 4;
                if (bl > level) code |= 8;

                if (code is 0 or 15)
                {
                    continue;
                }

                PointD Top() => new(x + Frac(tl, tr, level), y);
                PointD Bottom() => new(x + Frac(bl, br, level), y + 1);
                PointD Left() => new(x, y + Frac(tl, bl, level));
                PointD Right() => new(x + 1, y + Frac(tr, br, level));

                switch (code)
                {
                    case 1: case 14: segments.Add((Left(), Top())); break;
                    case 2: case 13: segments.Add((Top(), Right())); break;
                    case 3: case 12: segments.Add((Left(), Right())); break;
                    case 4: case 11: segments.Add((Right(), Bottom())); break;
                    case 6: case 9: segments.Add((Top(), Bottom())); break;
                    case 7: case 8: segments.Add((Left(), Bottom())); break;
                    // Saddle: two opposite corners are above the level, two below. Which pair the
                    // contour keeps together is set by the cell centre — and on a hard 0/1 mask, where
                    // the four corners average exactly to the level, by joining the above-level region,
                    // so a diagonal figure boundary stays one connected ring instead of breaking at
                    // every step of the staircase.
                    case 5:
                    case 10:
                    {
                        bool joinAbove = (tl + tr + br + bl) * 0.25 >= level;
                        bool tlBr = code == 5;
                        if (tlBr == joinAbove)
                        {
                            segments.Add((Top(), Right()));
                            segments.Add((Left(), Bottom()));
                        }
                        else
                        {
                            segments.Add((Left(), Top()));
                            segments.Add((Right(), Bottom()));
                        }

                        break;
                    }
                }
            }
        }

        return segments;
    }

    private static double Frac(double a, double b, double level)
    {
        double d = b - a;
        return Math.Abs(d) < 1e-9 ? 0.5 : Math.Clamp((level - a) / d, 0.0, 1.0);
    }

    /// <summary>
    /// Chains marching-squares segments whose endpoints coincide exactly — the case for a 0/1 mask,
    /// where every adjacent pair shares a vertex to the bit. Each vertex has two incident ends on a
    /// clean boundary, so the walk is unambiguous and every loop comes back closed. Unlike
    /// <see cref="Chain"/> it never welds across a near-miss, which on a flat run of a staircase
    /// boundary would short-circuit the loop into halves.
    /// </summary>
    private static IEnumerable<List<PointD>> ChainExact(List<(PointD A, PointD B)> segments)
    {
        static (long, long) Key(PointD p) =>
            ((long)Math.Round(p.X * 4096.0), (long)Math.Round(p.Y * 4096.0));

        var ends = new Dictionary<(long, long), List<int>>();
        void Register((long, long) key, int seg)
        {
            if (!ends.TryGetValue(key, out List<int>? list))
            {
                ends[key] = list = [];
            }

            list.Add(seg);
        }

        for (int i = 0; i < segments.Count; i++)
        {
            Register(Key(segments[i].A), i);
            Register(Key(segments[i].B), i);
        }

        var used = new bool[segments.Count];
        for (int start = 0; start < segments.Count; start++)
        {
            if (used[start])
            {
                continue;
            }

            var chain = new List<PointD> { segments[start].A, segments[start].B };
            used[start] = true;

            PointD head = segments[start].B;
            while (true)
            {
                if (!ends.TryGetValue(Key(head), out List<int>? here))
                {
                    break;
                }

                int next = -1;
                foreach (int k in here)
                {
                    if (!used[k])
                    {
                        next = k;
                        break;
                    }
                }

                if (next < 0)
                {
                    break;
                }

                used[next] = true;
                (PointD a, PointD b) = segments[next];
                head = Key(a) == Key(head) ? b : a;
                chain.Add(head);
            }

            yield return chain;
        }
    }

    private static IEnumerable<List<PointD>> Chain(List<(PointD A, PointD B)> segments, double weld)
    {
        segments.Sort((s, t) =>
        {
            int byY = s.A.Y.CompareTo(t.A.Y);
            return byY != 0 ? byY : s.A.X.CompareTo(t.A.X);
        });

        double weldSq = weld * weld;
        var used = new bool[segments.Count];

        static bool Near(PointD a, PointD b, double dSq) =>
            (((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y))) < dSq;

        for (int i = 0; i < segments.Count; i++)
        {
            if (used[i])
            {
                continue;
            }

            used[i] = true;
            var chain = new LinkedList<PointD>();
            chain.AddLast(segments[i].A);
            chain.AddLast(segments[i].B);

            bool grew = true;
            while (grew)
            {
                grew = false;
                for (int j = 0; j < segments.Count; j++)
                {
                    if (used[j])
                    {
                        continue;
                    }

                    (PointD a, PointD b) = segments[j];
                    if (Near(chain.Last!.Value, a, weldSq)) { chain.AddLast(b); used[j] = true; grew = true; }
                    else if (Near(chain.Last!.Value, b, weldSq)) { chain.AddLast(a); used[j] = true; grew = true; }
                    else if (Near(chain.First!.Value, a, weldSq)) { chain.AddFirst(b); used[j] = true; grew = true; }
                    else if (Near(chain.First!.Value, b, weldSq)) { chain.AddFirst(a); used[j] = true; grew = true; }
                }
            }

            yield return [.. chain];
        }
    }

    private static PointD[] DouglasPeucker(List<PointD> points, double epsilon)
    {
        if (points.Count < 3)
        {
            return [.. points];
        }

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;
        Simplify(points, 0, points.Count - 1, epsilon, keep);

        var result = new List<PointD>();
        for (int i = 0; i < points.Count; i++)
        {
            if (keep[i])
            {
                result.Add(points[i]);
            }
        }

        return [.. result];
    }

    private static void Simplify(List<PointD> pts, int first, int last, double epsilon, bool[] keep)
    {
        if (last <= first + 1)
        {
            return;
        }

        double maxDist = 0.0;
        int split = first;
        for (int i = first + 1; i < last; i++)
        {
            double d = PerpendicularDistance(pts[i], pts[first], pts[last]);
            if (d > maxDist)
            {
                maxDist = d;
                split = i;
            }
        }

        if (maxDist > epsilon)
        {
            keep[split] = true;
            Simplify(pts, first, split, epsilon, keep);
            Simplify(pts, split, last, epsilon, keep);
        }
    }

    private static double PerpendicularDistance(PointD p, PointD a, PointD b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double lenSq = (dx * dx) + (dy * dy);
        if (lenSq < 1e-12)
        {
            return Math.Sqrt(((p.X - a.X) * (p.X - a.X)) + ((p.Y - a.Y) * (p.Y - a.Y)));
        }

        double t = (((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / lenSq;
        double projX = a.X + (t * dx);
        double projY = a.Y + (t * dy);
        return Math.Sqrt(((p.X - projX) * (p.X - projX)) + ((p.Y - projY) * (p.Y - projY)));
    }

    private static double Length(IReadOnlyList<PointD> polyline)
    {
        double total = 0.0;
        for (int i = 1; i < polyline.Count; i++)
        {
            double dx = polyline[i].X - polyline[i - 1].X;
            double dy = polyline[i].Y - polyline[i - 1].Y;
            total += Math.Sqrt((dx * dx) + (dy * dy));
        }

        return total;
    }
}
