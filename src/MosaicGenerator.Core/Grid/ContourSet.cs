using MosaicGenerator.Core.Rendering;

namespace MosaicGenerator.Core.Grid;

/// <summary>
/// The lines a mosaicist would draw first: the subject's silhouette and its major internal
/// boundaries. Taken as the level set of the edge-strength field at a high percentile, traced with
/// marching squares, chained into polylines and simplified. Everything downstream — the contour
/// courses (opus vermiculatum) and the distance field that steers the background (opus musivum) —
/// is built from these.
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

        // Relative to how strong the strongest edges are, not to a fixed percentile: a thin
        // silhouette is only a percent or two of the pixels, so a percentile threshold would sit
        // in the flat background and find nothing.
        double strong = Percentile(edge, 0.99);
        if (strong < 0.15)
        {
            return [];
        }

        double level = Math.Max(0.12, strong * 0.42);

        var segments = MarchingSquares(edge, w, h, level);
        if (segments.Count == 0)
        {
            return [];
        }

        double sx = fieldWidthMm / Math.Max(1, w - 1);
        double sy = fieldHeightMm / Math.Max(1, h - 1);
        for (int i = 0; i < segments.Count; i++)
        {
            (PointD a, PointD b) = segments[i];
            segments[i] = (new PointD(a.X * sx, a.Y * sy), new PointD(b.X * sx, b.Y * sy));
        }

        double weld = moduleMm * 0.7;
        double minLength = moduleMm * 4.0;
        double epsilon = moduleMm * 0.75;

        var polylines = new List<PointD[]>();
        foreach (List<PointD> chain in Chain(segments, weld))
        {
            PointD[] simplified = DouglasPeucker(chain, epsilon);
            if (Length(simplified) >= minLength)
            {
                polylines.Add(simplified);
            }
        }

        polylines.Sort((p, q) =>
        {
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
                    case 5: segments.Add((Left(), Top())); segments.Add((Right(), Bottom())); break;
                    case 10: segments.Add((Top(), Right())); segments.Add((Left(), Bottom())); break;
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
