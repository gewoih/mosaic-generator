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
