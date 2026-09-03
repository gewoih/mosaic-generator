using MosaicGenerator.Core.Rendering;

namespace MosaicGenerator.Core.Grid;

/// <summary>
/// Distance from the nearest contour, plus the direction that runs parallel to that contour. This is
/// what makes the background read as opus musivum: every course, at every offset, echoes the shape it
/// surrounds instead of running straight.
///
/// The panel edge is deliberately not a barrier here. A mosaicist lays the border first and then fills
/// the field; the border is two real courses along the edge (see <see cref="Tessellation"/>), not a
/// pull on the field. Seeding the frame at distance zero made the whole open background echo the wall
/// instead of the subject — nested rectangles, a labyrinth.
/// </summary>
public sealed class DistanceField
{
    private readonly int _w;
    private readonly int _h;
    private readonly double[] _dist;
    private readonly double _maxDist;

    // Tangent to the iso-distance line as a double-angle vector, so it bilinear-interpolates
    // without the ±90° wrap.
    private readonly double[] _t2x;
    private readonly double[] _t2y;

    private DistanceField(int w, int h, double[] dist, double maxDist, double[] t2x, double[] t2y)
    {
        _w = w;
        _h = h;
        _dist = dist;
        _maxDist = maxDist;
        _t2x = t2x;
        _t2y = t2y;
    }

    /// <summary>Direction parallel to the nearest contour, radians, at field-normalised (u, v).</summary>
    public double TangentAt(double u, double v)
    {
        (double x2, double y2) = Bilerp2(_t2x, _t2y, u, v);
        return 0.5 * Math.Atan2(y2, x2);
    }

    /// <summary>Distance to the nearest contour as a fraction of the largest distance in the field, 0..1.</summary>
    public double NormalisedDistanceAt(double u, double v)
    {
        double fx = Math.Clamp(u, 0.0, 1.0) * (_w - 1);
        double fy = Math.Clamp(v, 0.0, 1.0) * (_h - 1);
        int x0 = (int)Math.Floor(fx);
        int y0 = (int)Math.Floor(fy);
        int x1 = Math.Min(x0 + 1, _w - 1);
        int y1 = Math.Min(y0 + 1, _h - 1);
        double tx = fx - x0;
        double ty = fy - y0;

        double top = Lerp(_dist[(y0 * _w) + x0], _dist[(y0 * _w) + x1], tx);
        double bottom = Lerp(_dist[(y1 * _w) + x0], _dist[(y1 * _w) + x1], tx);
        return Math.Clamp(Lerp(top, bottom, ty) / _maxDist, 0.0, 1.0);
    }

    /// <param name="tangentSmoothCells">
    /// Radius, in grid cells, over which the tangent field is smoothed. Set it from the course spacing:
    /// where two barriers meet, the tangent turns through 90° over nothing, and a course taken straight
    /// off that would spiral. Smoothing narrower than the course spacing does not touch it.
    /// </param>
    public static DistanceField Build(
        int width, int height, double fieldWidthMm, double fieldHeightMm,
        IReadOnlyList<PointD[]> contours, double tangentSmoothCells = 0.0)
    {
        var dist = new double[width * height];

        // A finite ceiling, larger than any real chamfer distance, so a region with no reachable
        // barrier stays finite through the blur instead of overflowing to infinity.
        double ceiling = (width + height) * 2.0;
        Array.Fill(dist, ceiling);

        double sx = (width - 1) / Math.Max(1e-9, fieldWidthMm);
        double sy = (height - 1) / Math.Max(1e-9, fieldHeightMm);

        foreach (PointD[] contour in contours)
        {
            for (int i = 1; i < contour.Length; i++)
            {
                RasterLine(
                    dist, width, height,
                    contour[i - 1].X * sx, contour[i - 1].Y * sy,
                    contour[i].X * sx, contour[i].Y * sy);
            }
        }

        Chamfer(dist, width, height);

        double maxDist = 1e-6;
        foreach (double d in dist)
        {
            maxDist = Math.Max(maxDist, d);
        }

        // Smooth before taking the gradient: a fragmented set of contours leaves the raw distance
        // field full of ridges, and the course direction taken straight off it would zig-zag.
        var smooth = (double[])dist.Clone();
        Blur(smooth, width, height, passes: 4);

        (double[] t2x, double[] t2y) = Tangents(smooth, width, height);

        // Double angles average without the ±90° wrap, so this is a legitimate mean of orientations.
        // Three box passes approximate a Gaussian; cells with no opinion carry (0, 0) and simply let
        // their neighbours decide.
        int radius = (int)Math.Round(tangentSmoothCells);
        if (radius > 0)
        {
            BoxBlur(t2x, width, height, radius);
            BoxBlur(t2y, width, height, radius);
        }

        return new DistanceField(width, height, dist, maxDist, t2x, t2y);
    }

    private static void RasterLine(double[] dist, int w, int h, double x0, double y0, double x1, double y1)
    {
        double dx = x1 - x0;
        double dy = y1 - y0;
        int steps = (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy))) + 1;
        for (int i = 0; i <= steps; i++)
        {
            double t = (double)i / steps;
            int gx = (int)Math.Round(x0 + (t * dx));
            int gy = (int)Math.Round(y0 + (t * dy));
            if (gx >= 0 && gx < w && gy >= 0 && gy < h)
            {
                dist[(gy * w) + gx] = 0.0;
            }
        }
    }

    private static void Chamfer(double[] dist, int w, int h)
    {
        const double ortho = 1.0;
        const double diag = 1.41421356237;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int k = (y * w) + x;
                double best = dist[k];
                if (x > 0) best = Math.Min(best, dist[k - 1] + ortho);
                if (y > 0) best = Math.Min(best, dist[k - w] + ortho);
                if (x > 0 && y > 0) best = Math.Min(best, dist[k - w - 1] + diag);
                if (x < w - 1 && y > 0) best = Math.Min(best, dist[k - w + 1] + diag);
                dist[k] = best;
            }
        }

        for (int y = h - 1; y >= 0; y--)
        {
            for (int x = w - 1; x >= 0; x--)
            {
                int k = (y * w) + x;
                double best = dist[k];
                if (x < w - 1) best = Math.Min(best, dist[k + 1] + ortho);
                if (y < h - 1) best = Math.Min(best, dist[k + w] + ortho);
                if (x < w - 1 && y < h - 1) best = Math.Min(best, dist[k + w + 1] + diag);
                if (x > 0 && y < h - 1) best = Math.Min(best, dist[k + w - 1] + diag);
                dist[k] = best;
            }
        }
    }

    private static void Blur(double[] f, int w, int h, int passes)
    {
        var scratch = new double[f.Length];
        for (int p = 0; p < passes; p++)
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int xm = Math.Max(0, x - 1);
                    int xp = Math.Min(w - 1, x + 1);
                    scratch[(y * w) + x] = (f[(y * w) + xm] + (2.0 * f[(y * w) + x]) + f[(y * w) + xp]) / 4.0;
                }
            }

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    int ym = Math.Max(0, y - 1);
                    int yp = Math.Min(h - 1, y + 1);
                    f[(y * w) + x] = (scratch[(ym * w) + x] + (2.0 * scratch[(y * w) + x]) + scratch[(yp * w) + x]) / 4.0;
                }
            }
        }
    }

    /// <summary>Three separable box passes — a cheap Gaussian, O(w·h) per pass whatever the radius.</summary>
    private static void BoxBlur(double[] f, int w, int h, int radius)
    {
        var scratch = new double[f.Length];
        double window = (2 * radius) + 1;

        for (int pass = 0; pass < 3; pass++)
        {
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                double sum = 0.0;
                for (int x = -radius; x <= radius; x++)
                {
                    sum += f[row + Math.Clamp(x, 0, w - 1)];
                }

                for (int x = 0; x < w; x++)
                {
                    scratch[row + x] = sum / window;
                    sum += f[row + Math.Clamp(x + radius + 1, 0, w - 1)];
                    sum -= f[row + Math.Clamp(x - radius, 0, w - 1)];
                }
            }

            for (int x = 0; x < w; x++)
            {
                double sum = 0.0;
                for (int y = -radius; y <= radius; y++)
                {
                    sum += scratch[(Math.Clamp(y, 0, h - 1) * w) + x];
                }

                for (int y = 0; y < h; y++)
                {
                    f[(y * w) + x] = sum / window;
                    sum += scratch[(Math.Clamp(y + radius + 1, 0, h - 1) * w) + x];
                    sum -= scratch[(Math.Clamp(y - radius, 0, h - 1) * w) + x];
                }
            }
        }
    }

    private static (double[] T2x, double[] T2y) Tangents(double[] dist, int w, int h)
    {
        var t2x = new double[w * h];
        var t2y = new double[w * h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int k = (y * w) + x;
                int xm = Math.Max(0, x - 1);
                int xp = Math.Min(w - 1, x + 1);
                int ym = Math.Max(0, y - 1);
                int yp = Math.Min(h - 1, y + 1);

                double gx = (dist[(y * w) + xp] - dist[(y * w) + xm]) / (xp - xm == 0 ? 1 : xp - xm);
                double gy = (dist[(yp * w) + x] - dist[(ym * w) + x]) / (yp - ym == 0 ? 1 : yp - ym);

                // Tangent is perpendicular to the gradient (which points away from the contour).
                double tx = -gy;
                double ty = gx;
                double len = Math.Sqrt((tx * tx) + (ty * ty));
                if (len < 1e-9)
                {
                    // A plateau of the chamfer field: no opinion. Leaving it at zero keeps it from
                    // dragging the smoothed field towards horizontal — the neighbours fill it in.
                    t2x[k] = 0.0;
                    t2y[k] = 0.0;
                    continue;
                }

                tx /= len;
                ty /= len;
                t2x[k] = (tx * tx) - (ty * ty);   // cos 2θ
                t2y[k] = 2.0 * tx * ty;            // sin 2θ
            }
        }

        return (t2x, t2y);
    }

    private (double X2, double Y2) Bilerp2(double[] ax, double[] ay, double u, double v)
    {
        double fx = Math.Clamp(u, 0.0, 1.0) * (_w - 1);
        double fy = Math.Clamp(v, 0.0, 1.0) * (_h - 1);
        int x0 = (int)Math.Floor(fx);
        int y0 = (int)Math.Floor(fy);
        int x1 = Math.Min(x0 + 1, _w - 1);
        int y1 = Math.Min(y0 + 1, _h - 1);
        double tx = fx - x0;
        double ty = fy - y0;

        double x2 = Lerp(
            Lerp(ax[(y0 * _w) + x0], ax[(y0 * _w) + x1], tx),
            Lerp(ax[(y1 * _w) + x0], ax[(y1 * _w) + x1], tx), ty);
        double y2 = Lerp(
            Lerp(ay[(y0 * _w) + x0], ay[(y0 * _w) + x1], tx),
            Lerp(ay[(y1 * _w) + x0], ay[(y1 * _w) + x1], tx), ty);
        return (x2, y2);
    }

    private static double Lerp(double a, double b, double t) => a + (t * (b - a));
}
