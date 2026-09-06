using MosaicGenerator.Core.Rendering;

namespace MosaicGenerator.Core.Grid;

/// <summary>Small polygon helpers shared by the tessellation and the sampler.</summary>
internal static class FieldGeometry
{
    /// <summary>Signed shoelace area; positive for a clockwise ring in screen coordinates.</summary>
    public static double Area(ReadOnlySpan<PointD> polygon)
    {
        double sum = 0.0;
        for (int i = 0; i < polygon.Length; i++)
        {
            PointD a = polygon[i];
            PointD b = polygon[(i + 1) % polygon.Length];
            sum += (a.X * b.Y) - (b.X * a.Y);
        }

        return Math.Abs(sum) / 2.0;
    }

    public static PointD Centroid(ReadOnlySpan<PointD> polygon)
    {
        double cx = 0.0, cy = 0.0, twiceArea = 0.0;
        for (int i = 0; i < polygon.Length; i++)
        {
            PointD a = polygon[i];
            PointD b = polygon[(i + 1) % polygon.Length];
            double cross = (a.X * b.Y) - (b.X * a.Y);
            twiceArea += cross;
            cx += (a.X + b.X) * cross;
            cy += (a.Y + b.Y) * cross;
        }

        if (Math.Abs(twiceArea) < 1e-12)
        {
            // Degenerate ring: fall back to the vertex average.
            double ax = 0.0, ay = 0.0;
            foreach (PointD p in polygon)
            {
                ax += p.X;
                ay += p.Y;
            }

            return new PointD(ax / polygon.Length, ay / polygon.Length);
        }

        return new PointD(cx / (3.0 * twiceArea), cy / (3.0 * twiceArea));
    }

    public static bool Contains(ReadOnlySpan<PointD> polygon, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            PointD a = polygon[i];
            PointD b = polygon[j];
            if (((a.Y > y) != (b.Y > y))
                && (x < ((b.X - a.X) * (y - a.Y) / (b.Y - a.Y)) + a.X))
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>
    /// Clips <paramref name="polygon"/> to the side of <paramref name="site"/>, cutting along the
    /// bisector between it and <paramref name="neighbour"/> pulled back by <paramref name="joint"/>.
    ///
    /// Run over every nearby neighbour this builds the site's Voronoi cell, held back by half a joint
    /// on every side: cells cannot overlap, cannot leave a gap between them, and always leave that
    /// joint. It is also how the pieces come out in the hand — a tessera is knapped to the space its
    /// neighbours leave it, which is why no two are the same shape.
    /// </summary>
    public static PointD[] ClipToBisector(
        IReadOnlyList<PointD> polygon, PointD site, PointD neighbour, double joint)
    {
        double dx = neighbour.X - site.X;
        double dy = neighbour.Y - site.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 1e-9)
        {
            return [.. polygon];
        }

        double nx = dx / length;
        double ny = dy / length;
        double offset = (length / 2.0) - joint;
        double px = site.X + (nx * offset);
        double py = site.Y + (ny * offset);

        double Side(PointD p) => ((p.X - px) * nx) + ((p.Y - py) * ny);

        var output = new List<PointD>(polygon.Count + 2);
        for (int i = 0; i < polygon.Count; i++)
        {
            PointD current = polygon[i];
            PointD previous = polygon[(i + polygon.Count - 1) % polygon.Count];
            double currentSide = Side(current);
            double previousSide = Side(previous);

            if (currentSide <= 0.0)
            {
                if (previousSide > 0.0)
                {
                    output.Add(Interpolate(previous, current, previousSide, currentSide));
                }

                output.Add(current);
            }
            else if (previousSide <= 0.0)
            {
                output.Add(Interpolate(previous, current, previousSide, currentSide));
            }
        }

        return [.. output];
    }

    /// <summary>
    /// Clips <paramref name="polygon"/> to the half-plane through
    /// <paramref name="site"/> + n·<paramref name="distance"/> with normal n, keeping the site's side.
    /// </summary>
    public static PointD[] ClipToLine(
        IReadOnlyList<PointD> polygon, PointD site, double nx, double ny, double distance)
    {
        double px = site.X + (nx * distance);
        double py = site.Y + (ny * distance);
        double Side(PointD p) => ((p.X - px) * nx) + ((p.Y - py) * ny);

        var output = new List<PointD>(polygon.Count + 2);
        for (int i = 0; i < polygon.Count; i++)
        {
            PointD current = polygon[i];
            PointD previous = polygon[(i + polygon.Count - 1) % polygon.Count];
            double currentSide = Side(current);
            double previousSide = Side(previous);

            if (currentSide <= 0.0)
            {
                if (previousSide > 0.0)
                {
                    output.Add(Interpolate(previous, current, previousSide, currentSide));
                }

                output.Add(current);
            }
            else if (previousSide <= 0.0)
            {
                output.Add(Interpolate(previous, current, previousSide, currentSide));
            }
        }

        return [.. output];
    }

    /// <summary>
    /// Squares a cut cell back up: drops vertices sitting on a near-straight run, then removes any
    /// remaining edge shorter than <paramref name="minEdgeMm"/> by running its two neighbouring
    /// edges on to where they cross — restoring the corner the cut shaved — until the polygon is a
    /// quadrilateral or has no short edge left. A tessera is knapped to a scored line and comes out
    /// four-sided; the Voronoi cut leaves a 1-3 mm nick across a corner where a neighbouring course
    /// meets this one at an angle, and that nick is both a sliver no hand can knap and the fifth
    /// vertex that makes the piece read as a pentagon (docs/forma-kuska-plan.md). The merged vertex
    /// lands partway towards the restored corner, so the piece keeps very nearly its area rather
    /// than losing the whole shaved triangle.
    /// </summary>
    public static PointD[] SquareOff(PointD[] polygon, double minEdgeMm)
    {
        if (polygon.Length <= 4)
        {
            return polygon;
        }

        var pts = new List<PointD>(polygon);

        // Vertices where the outline barely turns are clip noise, not corners of the piece.
        var kept = new List<PointD>(pts.Count);
        for (int i = 0; i < pts.Count; i++)
        {
            PointD a = pts[(i - 1 + pts.Count) % pts.Count];
            PointD b = pts[i];
            PointD c = pts[(i + 1) % pts.Count];
            if (DeflectionDegrees(a, b, c) >= 8.0)
            {
                kept.Add(b);
            }
        }

        if (kept.Count >= 3)
        {
            pts = kept;
        }

        // Then fold away the short nick edges, shortest first.
        while (pts.Count > 4)
        {
            int shortest = -1;
            double best = minEdgeMm;
            for (int i = 0; i < pts.Count; i++)
            {
                PointD p = pts[i];
                PointD q = pts[(i + 1) % pts.Count];
                double len = Math.Sqrt(((p.X - q.X) * (p.X - q.X)) + ((p.Y - q.Y) * (p.Y - q.Y)));
                if (len < best)
                {
                    best = len;
                    shortest = i;
                }
            }

            if (shortest < 0)
            {
                break;
            }

            int prev = (shortest - 1 + pts.Count) % pts.Count;
            int next = (shortest + 1) % pts.Count;
            int after = (shortest + 2) % pts.Count;

            // Fold the nick edge to one vertex. The full crossing of its two neighbouring edges is
            // the corner the cut shaved, but restoring it whole overshoots into the course that made
            // the cut (measured: panel overlap past a tile-test tolerance). Folding to the plain
            // midpoint instead throws away the shaved triangle and opens a joint. Land partway
            // between — measured across the sample set, four tenths of the way to the corner squares
            // the piece while keeping panel overlap and coverage inside the tile test.
            PointD? corner = LineCross(pts[prev], pts[shortest], pts[after], pts[next]);
            PointD mid = new(
                (pts[shortest].X + pts[next].X) / 2.0,
                (pts[shortest].Y + pts[next].Y) / 2.0);
            PointD replacement = mid;
            if (corner is { } x && Distance(x, mid) < best * 4.0)
            {
                const double Restore = 0.4;
                replacement = new PointD(
                    mid.X + ((x.X - mid.X) * Restore),
                    mid.Y + ((x.Y - mid.Y) * Restore));
            }

            pts[shortest] = replacement;
            pts.RemoveAt(next);
        }

        return pts.Count >= 3 ? [.. pts] : polygon;
    }

    private static double Distance(PointD a, PointD b) =>
        Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    /// <summary>Where line a1->a2 crosses line b1->b2, or null if they are parallel.</summary>
    private static PointD? LineCross(PointD a1, PointD a2, PointD b1, PointD b2)
    {
        double d1x = a2.X - a1.X, d1y = a2.Y - a1.Y;
        double d2x = b2.X - b1.X, d2y = b2.Y - b1.Y;
        double denom = (d1x * d2y) - (d1y * d2x);
        if (Math.Abs(denom) < 1e-9)
        {
            return null;
        }

        double t = (((b1.X - a1.X) * d2y) - ((b1.Y - a1.Y) * d2x)) / denom;
        return new PointD(a1.X + (t * d1x), a1.Y + (t * d1y));
    }

    /// <summary>How far the outline turns at <paramref name="b"/>, in degrees; 0 is straight through.</summary>
    private static double DeflectionDegrees(PointD a, PointD b, PointD c)
    {
        double ux = b.X - a.X, uy = b.Y - a.Y;
        double vx = c.X - b.X, vy = c.Y - b.Y;
        double lu = Math.Sqrt((ux * ux) + (uy * uy));
        double lv = Math.Sqrt((vx * vx) + (vy * vy));
        if (lu < 1e-9 || lv < 1e-9)
        {
            return 0.0;
        }

        double cos = Math.Clamp(((ux * vx) + (uy * vy)) / (lu * lv), -1.0, 1.0);
        return Math.Acos(cos) * 180.0 / Math.PI;
    }

    private static PointD Interpolate(PointD a, PointD b, double sa, double sb)
    {
        double t = sa / (sa - sb);
        return new PointD(a.X + (t * (b.X - a.X)), a.Y + (t * (b.Y - a.Y)));
    }

    /// <summary>Sutherland–Hodgman clip of a convex-ish polygon to an axis-aligned rectangle.</summary>
    public static PointD[] ClipToRect(IReadOnlyList<PointD> polygon, double minX, double minY, double maxX, double maxY)
    {
        List<PointD> output = [.. polygon];
        output = ClipEdge(output, p => p.X >= minX, (a, b) => LerpX(a, b, minX));
        output = ClipEdge(output, p => p.X <= maxX, (a, b) => LerpX(a, b, maxX));
        output = ClipEdge(output, p => p.Y >= minY, (a, b) => LerpY(a, b, minY));
        output = ClipEdge(output, p => p.Y <= maxY, (a, b) => LerpY(a, b, maxY));
        return [.. output];
    }

    private static List<PointD> ClipEdge(
        List<PointD> input, Func<PointD, bool> inside, Func<PointD, PointD, PointD> intersect)
    {
        var output = new List<PointD>(input.Count + 2);
        if (input.Count == 0)
        {
            return output;
        }

        for (int i = 0; i < input.Count; i++)
        {
            PointD current = input[i];
            PointD previous = input[(i + input.Count - 1) % input.Count];
            bool currentIn = inside(current);
            bool previousIn = inside(previous);

            if (currentIn)
            {
                if (!previousIn)
                {
                    output.Add(intersect(previous, current));
                }

                output.Add(current);
            }
            else if (previousIn)
            {
                output.Add(intersect(previous, current));
            }
        }

        return output;
    }

    private static PointD LerpX(PointD a, PointD b, double x)
    {
        double t = Math.Abs(b.X - a.X) < 1e-12 ? 0.0 : (x - a.X) / (b.X - a.X);
        return new PointD(x, a.Y + (t * (b.Y - a.Y)));
    }

    private static PointD LerpY(PointD a, PointD b, double y)
    {
        double t = Math.Abs(b.Y - a.Y) < 1e-12 ? 0.0 : (y - a.Y) / (b.Y - a.Y);
        return new PointD(a.X + (t * (b.X - a.X)), y);
    }
}
