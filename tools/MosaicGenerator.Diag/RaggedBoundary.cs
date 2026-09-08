using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Grid;
using MosaicGenerator.Core.Rendering;
using SkiaSharp;

namespace MosaicGenerator.Diag;

/// <summary>
/// The form of a tonal step, which <c>banding</c> cannot see. <c>banding</c> counts pairs of
/// neighbours the photograph gives one colour and the palette gives two; a clean straight border
/// between two shades and a torn zigzag blotch score the same. This measures the second thing —
/// how far the border between two articles wanders where the photograph underneath is flat.
///
/// Only flat stretches count: the criterion is <c>banding</c>'s own (photo ΔE &lt; 2 across the
/// border), so a real subject edge — a beak, the rim of a pair of glasses — is excluded on its
/// own, and the number stays paired with <c>banding</c> by construction. Only borders between two
/// large same-article regions count (each ≥ 12 module²): speckle is крап, owned by
/// <c>singleton</c> / <c>smallIsland</c> / <c>midIsland</c>, not this.
///
/// The measure is mean absolute turning of the border, in radians per module, after simplifying it
/// at the scale of one piece (features finer than a tessera are not the mosaicist's choice). A
/// straight step — axis-aligned or diagonal — simplifies to one segment and turns nowhere: ~0. A
/// torn blotch keeps reversing direction every module or two: high.
/// </summary>
internal static class RaggedBoundary
{
    private const double FlatDe = 2.0;
    private const double MinRegionModules2 = 12.0;

    /// <summary>
    /// Douglas–Peucker tolerance, in modules. Two pieces: wander finer than this is rasterisation
    /// of a straight sloped border, not a choice the mosaicist made — a monotone stair of one-module
    /// steps down a gentle slope collapses to one segment and scores nothing, which is the point of
    /// пункт 22 (a clean step is not the defect). A torn blotch stays torn above two modules.
    /// </summary>
    private const double SimplifyModules = 2.0;

    internal readonly record struct Segment(PointD A, PointD B, bool Unsupported, bool BigRegions);

    internal sealed record Result(double RadPerModule, IReadOnlyList<Segment> Segments);

    public static Result Measure(
        MosaicLayout layout,
        IReadOnlyList<Tessera> tesserae,
        IReadOnlyList<LinearRgb> cells,
        IReadOnlyList<int> indices)
    {
        double cell = layout.ModuleWidthMm / 6.0;
        int w = (int)Math.Ceiling(layout.FieldWidthMm / cell);
        int h = (int)Math.Ceiling(layout.FieldHeightMm / cell);

        // Article per raster pixel, −1 where no tessera covers it.
        var art = new int[w * h];
        Array.Fill(art, -1);
        var owner = new int[w * h];
        Array.Fill(owner, -1);
        for (int ti = 0; ti < tesserae.Count; ti++)
        {
            PointD[] poly = tesserae[ti].Polygon;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (PointD p in poly)
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
                    if (Contains(poly, (x + 0.5) * cell, (y + 0.5) * cell))
                    {
                        art[(y * w) + x] = indices[ti];
                        owner[(y * w) + x] = ti;
                    }
                }
            }
        }

        // The joint leaves a bare pixel or two between neighbouring tesserae, so east/south of a
        // covered pixel is usually bare, not the other article, and no border is ever found. Fill
        // every bare pixel with its nearest tessera by a multi-source flood, so the two shades meet.
        {
            var queue = new Queue<int>();
            for (int k = 0; k < art.Length; k++)
            {
                if (owner[k] >= 0)
                {
                    queue.Enqueue(k);
                }
            }

            while (queue.Count > 0)
            {
                int k = queue.Dequeue();
                int x = k % w, y = k / w;
                Spread(k - 1, x > 0);
                Spread(k + 1, x < w - 1);
                Spread(k - w, y > 0);
                Spread(k + w, y < h - 1);

                void Spread(int n, bool inBounds)
                {
                    if (inBounds && owner[n] < 0)
                    {
                        owner[n] = owner[k];
                        art[n] = art[k];
                        queue.Enqueue(n);
                    }
                }
            }
        }

        var lab = new CieLab[tesserae.Count];
        for (int i = 0; i < tesserae.Count; i++)
        {
            lab[i] = cells[i].ToLab();
        }

        // Same-article connected regions and their area, in module².
        double module2 = layout.ModuleWidthMm * layout.ModuleHeightMm;
        int[] region = LabelRegions(art, w, h, out int[] regionPixels);
        var bigRegion = new bool[regionPixels.Length];
        for (int r = 0; r < regionPixels.Length; r++)
        {
            bigRegion[r] = regionPixels[r] * cell * cell / module2 >= MinRegionModules2;
        }

        // Border segments on the pixel grid, in corner coordinates (units of one pixel).
        var adjacency = new Dictionary<(int, int), List<((int, int) To, bool Unsupported, bool Big)>>();
        var segments = new List<Segment>();

        void Edge(int ka, int kb, (int, int) c0, (int, int) c1)
        {
            if (art[ka] < 0 || art[kb] < 0 || art[ka] == art[kb])
            {
                return;
            }

            bool unsupported = ColorDistance.CieDe76(lab[owner[ka]], lab[owner[kb]]) < FlatDe;
            bool big = bigRegion[region[ka]] && bigRegion[region[kb]];
            segments.Add(new Segment(
                new PointD(c0.Item1 * cell, c0.Item2 * cell),
                new PointD(c1.Item1 * cell, c1.Item2 * cell),
                unsupported, big));

            if (!big)
            {
                return;
            }

            Link(c0, c1, unsupported);
            Link(c1, c0, unsupported);
        }

        void Link((int, int) from, (int, int) to, bool unsupported)
        {
            if (!adjacency.TryGetValue(from, out var list))
            {
                adjacency[from] = list = [];
            }

            list.Add((to, unsupported, true));
        }

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int k = (y * w) + x;
                if (x + 1 < w)
                {
                    Edge(k, k + 1, (x + 1, y), (x + 1, y + 1));
                }

                if (y + 1 < h)
                {
                    Edge(k, k + w, (x, y + 1), (x + 1, y + 1));
                }
            }
        }

        // Trace the border into chains, walking through degree-2 corners.
        var visited = new HashSet<((int, int), (int, int))>();
        double turnSum = 0.0;
        double lenModules = 0.0;

        foreach ((int, int) start in adjacency.Keys)
        {
            if (adjacency[start].Count == 2)
            {
                continue;   // pick chains up from their ends and from junctions
            }

            foreach (var first in adjacency[start])
            {
                var chain = WalkChain(start, first.To, adjacency, visited);
                if (chain is null)
                {
                    continue;
                }

                Accumulate(chain, cell, layout.ModuleWidthMm, ref turnSum, ref lenModules);
            }
        }

        // Pure loops left over: no degree-≠2 corner to seed from.
        foreach ((int, int) start in adjacency.Keys)
        {
            foreach (var first in adjacency[start])
            {
                var chain = WalkChain(start, first.To, adjacency, visited);
                if (chain is null)
                {
                    continue;
                }

                Accumulate(chain, cell, layout.ModuleWidthMm, ref turnSum, ref lenModules);
            }
        }

        return new Result(lenModules > 1e-6 ? turnSum / lenModules : 0.0, segments);
    }

    private static List<(int, int)>? WalkChain(
        (int, int) start,
        (int, int) next,
        Dictionary<(int, int), List<((int, int) To, bool Unsupported, bool Big)>> adjacency,
        HashSet<((int, int), (int, int))> visited)
    {
        var edge = Ordered(start, next);
        if (!visited.Add(edge))
        {
            return null;
        }

        var pts = new List<(int, int)> { start, next };
        var unsupportedLen = SegLen(start, next) * (IsUnsupported(adjacency, start, next) ? 1.0 : 0.0);
        var totalLen = SegLen(start, next);
        (int, int) prev = start, cur = next;

        while (adjacency.TryGetValue(cur, out var outs) && outs.Count == 2)
        {
            var step = outs[0].To.Equals(prev) ? outs[1] : outs[0];
            if (step.To.Equals(prev))
            {
                break;
            }

            var e = Ordered(cur, step.To);
            if (!visited.Add(e))
            {
                break;
            }

            pts.Add(step.To);
            double sl = SegLen(cur, step.To);
            totalLen += sl;
            if (step.Unsupported)
            {
                unsupportedLen += sl;
            }

            prev = cur;
            cur = step.To;
            if (cur.Equals(start))
            {
                break;   // closed loop
            }
        }

        return totalLen > 0.0 && unsupportedLen / totalLen >= 0.5 ? pts : null;
    }

    private static bool IsUnsupported(
        Dictionary<(int, int), List<((int, int) To, bool Unsupported, bool Big)>> adjacency,
        (int, int) a,
        (int, int) b)
    {
        foreach (var e in adjacency[a])
        {
            if (e.To.Equals(b))
            {
                return e.Unsupported;
            }
        }

        return false;
    }

    private static void Accumulate(
        List<(int, int)> chain, double cell, double moduleMm, ref double turnSum, ref double lenModules)
    {
        var mm = new List<PointD>(chain.Count);
        foreach ((int cx, int cy) in chain)
        {
            mm.Add(new PointD(cx * cell, cy * cell));
        }

        List<PointD> simple = Simplify(mm, SimplifyModules * moduleMm);
        if (simple.Count < 2)
        {
            return;
        }

        for (int i = 1; i < simple.Count; i++)
        {
            double dx = simple[i].X - simple[i - 1].X;
            double dy = simple[i].Y - simple[i - 1].Y;
            lenModules += Math.Sqrt((dx * dx) + (dy * dy)) / moduleMm;
        }

        for (int i = 1; i + 1 < simple.Count; i++)
        {
            PointD a = Unit(simple[i], simple[i - 1]);
            PointD b = Unit(simple[i + 1], simple[i]);
            double dot = Math.Clamp((a.X * b.X) + (a.Y * b.Y), -1.0, 1.0);
            turnSum += Math.Acos(dot);
        }
    }

    private static PointD Unit(PointD to, PointD from)
    {
        double dx = to.X - from.X, dy = to.Y - from.Y;
        double len = Math.Sqrt((dx * dx) + (dy * dy));
        return len < 1e-9 ? new PointD(0.0, 0.0) : new PointD(dx / len, dy / len);
    }

    /// <summary>Douglas–Peucker, tolerance one module — drop wander finer than a piece.</summary>
    private static List<PointD> Simplify(List<PointD> pts, double epsilon)
    {
        if (pts.Count < 3)
        {
            return pts;
        }

        var keep = new bool[pts.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, pts.Count - 1));
        while (stack.Count > 0)
        {
            (int lo, int hi) = stack.Pop();
            double worst = 0.0;
            int at = -1;
            for (int i = lo + 1; i < hi; i++)
            {
                double d = PerpDistance(pts[i], pts[lo], pts[hi]);
                if (d > worst)
                {
                    worst = d;
                    at = i;
                }
            }

            if (at >= 0 && worst > epsilon)
            {
                keep[at] = true;
                stack.Push((lo, at));
                stack.Push((at, hi));
            }
        }

        var outp = new List<PointD>();
        for (int i = 0; i < pts.Count; i++)
        {
            if (keep[i])
            {
                outp.Add(pts[i]);
            }
        }

        return outp;
    }

    private static double PerpDistance(PointD p, PointD a, PointD b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len = Math.Sqrt((dx * dx) + (dy * dy));
        if (len < 1e-9)
        {
            double ex = p.X - a.X, ey = p.Y - a.Y;
            return Math.Sqrt((ex * ex) + (ey * ey));
        }

        return Math.Abs(((p.X - a.X) * dy) - ((p.Y - a.Y) * dx)) / len;
    }

    private static double SegLen((int, int) a, (int, int) b) =>
        Math.Abs(a.Item1 - b.Item1) + Math.Abs(a.Item2 - b.Item2);

    private static ((int, int), (int, int)) Ordered((int, int) a, (int, int) b) =>
        (a.Item1, a.Item2).CompareTo((b.Item1, b.Item2)) <= 0 ? (a, b) : (b, a);

    /// <summary>Flood-fill same-article 4-connected regions; returns a region id per pixel.</summary>
    private static int[] LabelRegions(int[] art, int w, int h, out int[] regionPixels)
    {
        var region = new int[w * h];
        Array.Fill(region, -1);
        var sizes = new List<int>();
        var queue = new Queue<int>();
        for (int s = 0; s < art.Length; s++)
        {
            if (art[s] < 0 || region[s] >= 0)
            {
                continue;
            }

            int id = sizes.Count;
            int count = 0;
            region[s] = id;
            queue.Enqueue(s);
            while (queue.Count > 0)
            {
                int k = queue.Dequeue();
                count++;
                int x = k % w, y = k / w;
                Visit(k - 1, x > 0);
                Visit(k + 1, x < w - 1);
                Visit(k - w, y > 0);
                Visit(k + w, y < h - 1);

                void Visit(int n, bool inBounds)
                {
                    if (inBounds && region[n] < 0 && art[n] == art[k])
                    {
                        region[n] = id;
                        queue.Enqueue(n);
                    }
                }
            }

            sizes.Add(count);
        }

        regionPixels = [.. sizes];
        return region;
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

    /// <summary>
    /// The border, drawn where it runs. Red is border in a flat stretch between two large regions —
    /// what the metric counts; faint grey is border with support in the photograph or against a
    /// small region. Not a cartoon: a picture to read the defect off, like <see cref="HoleMap"/>.
    /// </summary>
    public static void Write(string path, MosaicLayout layout, IReadOnlyList<Tessera> tesserae, Result result)
    {
        const double pxPerMm = 8.0;
        int w = (int)Math.Ceiling(layout.FieldWidthMm * pxPerMm);
        int h = (int)Math.Ceiling(layout.FieldHeightMm * pxPerMm);
        using var surface = SKSurface.Create(new SKImageInfo(w, h));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(0x20, 0x20, 0x22));

        using var fill = new SKPaint { Color = new SKColor(0x30, 0x30, 0x34), IsAntialias = true };
        foreach (Tessera t in tesserae)
        {
            using var p = new SKPath();
            p.MoveTo((float)(t.Polygon[0].X * pxPerMm), (float)(t.Polygon[0].Y * pxPerMm));
            for (int i = 1; i < t.Polygon.Length; i++)
            {
                p.LineTo((float)(t.Polygon[i].X * pxPerMm), (float)(t.Polygon[i].Y * pxPerMm));
            }

            p.Close();
            canvas.DrawPath(p, fill);
        }

        using var faint = new SKPaint
        {
            Color = new SKColor(0x70, 0x70, 0x78),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
        };
        using var hot = new SKPaint
        {
            Color = new SKColor(0xE0, 0x30, 0x30),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f,
        };

        foreach (Segment s in result.Segments)
        {
            SKPaint paint = s is { Unsupported: true, BigRegions: true } ? hot : faint;
            canvas.DrawLine(
                (float)(s.A.X * pxPerMm), (float)(s.A.Y * pxPerMm),
                (float)(s.B.X * pxPerMm), (float)(s.B.Y * pxPerMm), paint);
        }

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream file = File.Create(path);
        data.SaveTo(file);
    }
}
