using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Grid;
using MosaicGenerator.Core.Rendering;

namespace MosaicGenerator.Diag;

/// <summary>
/// Which parts of the field a layout actually covers, rasterised from the tessera polygons.
/// Same idea as the one in the test project — kept separate so the diagnostics tool does not
/// depend on the tests.
/// </summary>
internal sealed class CoverageMask
{
    private readonly bool[] _covered;
    private readonly int[] _hits;
    private readonly int _w;
    private readonly int _h;
    private readonly double _cell;

    private CoverageMask(bool[] covered, int[] hits, int w, int h, double cell)
    {
        _covered = covered;
        _hits = hits;
        _w = w;
        _h = h;
        _cell = cell;
    }

    public static CoverageMask Rasterise(MosaicLayout layout, IReadOnlyList<Tessera> tesserae) =>
        Rasterise(layout, tesserae, layout.ModuleWidthMm / 6.0);

    public static CoverageMask Rasterise(MosaicLayout layout, IReadOnlyList<Tessera> tesserae, double cell)
    {
        int w = (int)Math.Ceiling(layout.FieldWidthMm / cell);
        int h = (int)Math.Ceiling(layout.FieldHeightMm / cell);
        var covered = new bool[w * h];
        var hits = new int[w * h];

        foreach (Tessera tessera in tesserae)
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (PointD p in tessera.Polygon)
            {
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
            }

            int x0 = Math.Max(0, (int)Math.Floor(minX / cell));
            int y0 = Math.Max(0, (int)Math.Floor(minY / cell));
            int x1 = Math.Min(w - 1, (int)Math.Ceiling(maxX / cell));
            int y1 = Math.Min(h - 1, (int)Math.Ceiling(maxY / cell));

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (Contains(tessera.Polygon, (x + 0.5) * cell, (y + 0.5) * cell))
                    {
                        covered[(y * w) + x] = true;
                        hits[(y * w) + x]++;
                    }
                }
            }
        }

        return new CoverageMask(covered, hits, w, h, cell);
    }

    public double OverlappedFraction() => (double)_hits.Count(h => h > 1) / _hits.Length;

    public double CoveredFraction() => (double)_covered.Count(c => c) / _covered.Length;

    public (double RadiusMm, PointD Where) LargestBare()
    {
        double[] d = Clearance();
        double worst = 0.0;
        int at = 0;
        for (int i = 0; i < d.Length; i++)
        {
            if (d[i] > worst)
            {
                worst = d[i];
                at = i;
            }
        }

        return (worst * _cell, new PointD(((at % _w) + 0.5) * _cell, ((at / _w) + 0.5) * _cell));
    }

    /// <summary>Share of the field lying more than <paramref name="radiusMm"/> from any tessera.</summary>
    public double BareBeyond(double radiusMm)
    {
        double[] d = Clearance();
        return (double)d.Count(c => c * _cell > radiusMm) / d.Length;
    }

    /// <summary>
    /// The joint seen as gaps rather than as a mean: for every uncovered point, twice its distance
    /// to the nearest tessera — the width of the gap it sits in. Area-weighted percentiles, one
    /// sample per raster cell. This runs its own fine raster (module / 40 ≈ 0.25 mm) rather than
    /// the module / 6 grid the coverage numbers use — a 1 mm joint is invisible at module / 6.
    /// <paramref name="wideMm"/> is the width past which a gap reads as a hole on the cartoon; its
    /// share of the whole field comes back as <c>WideArea</c>.
    /// </summary>
    public static (double P50Mm, double P90Mm, double MaxMm, double WideArea, double JointArea) JointWidths(
        MosaicLayout layout, IReadOnlyList<Tessera> tesserae, double wideMm)
    {
        CoverageMask fine = Rasterise(layout, tesserae, layout.ModuleWidthMm / 40.0);
        double[] d = fine.Clearance();
        double[] gaps = [.. d.Where(c => c > 0.0).Select(c => 2.0 * c * fine._cell).Order()];
        if (gaps.Length == 0)
        {
            return (0.0, 0.0, 0.0, 0.0, 0.0);
        }

        double P(double q) => gaps[Math.Clamp((int)(q * (gaps.Length - 1)), 0, gaps.Length - 1)];
        return (
            P(0.50), P(0.90), gaps[^1],
            (double)gaps.Count(g => g > wideMm) / d.Length,
            (double)gaps.Length / d.Length);
    }

    private static bool Contains(IReadOnlyList<PointD> polygon, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
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

    private double[] Clearance()
    {
        double ceiling = _w + _h;
        var d = new double[_w * _h];
        for (int i = 0; i < d.Length; i++)
        {
            d[i] = _covered[i] ? 0.0 : ceiling;
        }

        const double Diag = 1.41421356237;
        for (int y = 0; y < _h; y++)
        {
            for (int x = 0; x < _w; x++)
            {
                int k = (y * _w) + x;
                if (x > 0) d[k] = Math.Min(d[k], d[k - 1] + 1.0);
                if (y > 0) d[k] = Math.Min(d[k], d[k - _w] + 1.0);
                if (x > 0 && y > 0) d[k] = Math.Min(d[k], d[k - _w - 1] + Diag);
                if (x < _w - 1 && y > 0) d[k] = Math.Min(d[k], d[k - _w + 1] + Diag);
            }
        }

        for (int y = _h - 1; y >= 0; y--)
        {
            for (int x = _w - 1; x >= 0; x--)
            {
                int k = (y * _w) + x;
                if (x < _w - 1) d[k] = Math.Min(d[k], d[k + 1] + 1.0);
                if (y < _h - 1) d[k] = Math.Min(d[k], d[k + _w] + 1.0);
                if (x < _w - 1 && y < _h - 1) d[k] = Math.Min(d[k], d[k + _w + 1] + Diag);
                if (x > 0 && y < _h - 1) d[k] = Math.Min(d[k], d[k + _w - 1] + Diag);
            }
        }

        return d;
    }
}
