using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Grid;
using MosaicGenerator.Core.Rendering;

namespace MosaicGenerator.Core.Tests.Support;

/// <summary>
/// Which parts of the field a layout actually covers, rasterised from the tessera polygons.
///
/// Summed tessera area does not answer this: courses overlap slightly by design, and that overlap
/// hides bare channels in the total. A wall does not care about the total — it cares whether there
/// is adhesive showing.
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

    public static CoverageMask Rasterise(MosaicLayout layout, IReadOnlyList<Tessera> tesserae)
    {
        double cell = layout.ModuleWidthMm / 6.0;
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

    /// <summary>Share of the field claimed by more than one tessera — smalt pieces never overlap.</summary>
    public double OverlappedFraction()
    {
        int over = 0;
        foreach (int h in _hits)
        {
            if (h > 1)
            {
                over++;
            }
        }

        return (double)over / _hits.Length;
    }

    public double CoveredFraction()
    {
        int hit = 0;
        foreach (bool c in _covered)
        {
            if (c)
            {
                hit++;
            }
        }

        return (double)hit / _covered.Length;
    }

    /// <summary>Radius, in millimetres, of the largest bare disc that fits in the layout.</summary>
    public double LargestBareRadiusMm() => LargestBare().RadiusMm;

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

    /// <summary>Chamfer distance from every cell to the nearest covered cell, in cells.</summary>
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

    /// <summary>Share of the field held by bare cells whose clearance exceeds 0.2 / 0.3 / 0.45 / 0.6 module.</summary>
    public double[] BareClearanceHistogram(double module)
    {
        double[] d = Clearance();
        double[] thresholds = [0.2, 0.3, 0.45, 0.6];
        var counts = new double[thresholds.Length];
        for (int i = 0; i < d.Length; i++)
        {
            for (int k = 0; k < thresholds.Length; k++)
            {
                if (d[i] * _cell >= thresholds[k] * module)
                {
                    counts[k]++;
                }
            }
        }

        for (int k = 0; k < counts.Length; k++)
        {
            counts[k] /= d.Length;
        }

        return counts;
    }

    /// <summary>Share of the field lying in bare patches more than <paramref name="radiusMm"/> from any tessera.</summary>
    public double BareBeyond(double radiusMm)
    {
        double[] d = Clearance();
        int count = 0;
        foreach (double c in d)
        {
            if (c * _cell > radiusMm)
            {
                count++;
            }
        }

        return (double)count / d.Length;
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
}
