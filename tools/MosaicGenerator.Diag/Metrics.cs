using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Grid;
using MosaicGenerator.Core.Rendering;

namespace MosaicGenerator.Diag;

/// <summary>
/// Everything the eye cannot count. The layout metrics ask whether the pieces could actually be
/// cut and set; the colour metrics ask whether the palette can carry the photograph.
/// </summary>
internal static class Metrics
{
    internal sealed record Shape
    {
        public required double AreaMin { get; init; }
        public required double AreaP5 { get; init; }
        public required double AreaMedian { get; init; }
        public required double AreaP95 { get; init; }
        public required double AreaMax { get; init; }

        /// <summary>Share of pieces smaller than 0.4 module² — below what a hammer can cut.</summary>
        public required double TinyShare { get; init; }

        /// <summary>Share of pieces whose bounding box in the course axes is more than 3:1 — splinters.</summary>
        public required double SliverShare { get; init; }

        /// <summary>Share of pieces with more than six vertices — shards no cut produces.</summary>
        public required double ManySidedShare { get; init; }

        /// <summary>Share of joints along a course that turn by more than 25° — the course reads as broken.</summary>
        public required double KinkShare { get; init; }

        public required int CourseCount { get; init; }

        /// <summary>
        /// Share of pieces that belong to no course — the singles grown into bare spots in step 5.
        /// They are legitimate work, the piece a mosaicist slips in where two runs did not meet, but
        /// they are not a run, so every course metric below is measured without them.
        /// </summary>
        public required double FillerShare { get; init; }

        /// <summary>Share of courses shorter than three pieces — scattered singles, not a course.</summary>
        public required double StubCourseShare { get; init; }

        public required double MedianCourseLength { get; init; }

        /// <summary>Narrowest side of the piece in millimetres, 5th percentile — what the nippers must hit.</summary>
        public required double MinSideP5 { get; init; }

        /// <summary>Share of pieces narrower than 3 mm on their short side — nothing cuts that.</summary>
        public required double UncuttableShare { get; init; }

        /// <summary>Share narrower than 5 mm — below what nippers reliably produce from 7 mm smalt.</summary>
        public required double AwkwardShare { get; init; }

        /// <summary>
        /// Median angle, in degrees, between a course and the direction the photograph's own texture
        /// runs at that point. A mosaicist lays courses along the form; a layout that ignores the
        /// picture is laying them across it.
        /// </summary>
        public required double StructureDisagreement { get; init; }

        /// <summary>
        /// Share of pieces sitting on a strong edge whose course runs across it rather than along it.
        /// A course crossing an edge puts every piece astride it, and the edge is smeared away —
        /// this is what erases a mountain ridge or a treeline.
        /// </summary>
        public required double EdgesCrossed { get; init; }
    }

    public static Shape Shapes(
        MosaicLayout layout, IReadOnlyList<Tessera> tesserae, DirectionField? field = null,
        CourseGuidance? guidance = null)
    {
        double module2 = layout.ModuleWidthMm * layout.ModuleHeightMm;

        double[] areas = [.. tesserae.Select(t => Math.Abs(t.AreaMm2) / module2).Order()];

        // Course axis from the neighbours along the same course; falls back to the longest edge.
        var byCourse = tesserae
            .GroupBy(t => t.CourseId)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.IndexInCourse).ToArray());

        var minSides = new List<double>(tesserae.Count);
        int slivers = 0;
        int manySided = 0;
        int kinks = 0;
        int joints = 0;

        foreach (Tessera[] course in byCourse.Values)
        {
            for (int i = 0; i < course.Length; i++)
            {
                PointD axis = CourseAxis(course, i);
                (double along, double across) = Extent(course[i].Polygon, axis);
                minSides.Add(Math.Min(along, across));
                if (Math.Max(along, across) / Math.Max(1e-6, Math.Min(along, across)) > 3.0)
                {
                    slivers++;
                }

                if (course[i].Polygon.Length > 6)
                {
                    manySided++;
                }
            }

            if (course[0].CourseId < 0)
            {
                continue;   // a filler is one piece and has no joints of its own
            }

            for (int i = 1; i + 1 < course.Length; i++)
            {
                PointD a = Unit(Sub(course[i].Centroid, course[i - 1].Centroid));
                PointD b = Unit(Sub(course[i + 1].Centroid, course[i].Centroid));
                double dot = Math.Clamp((a.X * b.X) + (a.Y * b.Y), -1.0, 1.0);
                joints++;
                if (Math.Acos(dot) > 25.0 * Math.PI / 180.0)
                {
                    kinks++;
                }
            }
        }

        var disagreements = new List<double>();
        int onEdge = 0;
        int crossing = 0;

        if (field is not null)
        {
            foreach (Tessera[] course in byCourse.Values)
            {
                for (int i = 0; i < course.Length; i++)
                {
                    PointD axis = CourseAxis(course, i);
                    double u = course[i].Centroid.X / layout.FieldWidthMm;
                    double v = course[i].Centroid.Y / layout.FieldHeightMm;

                    // Against the field the courses were actually laid along, not the diffused one.
                    // Comparing to DirectionField.ThetaAt measured the gap between two different
                    // fields as if it were a course going the wrong way.
                    double theta = guidance is null ? field.ThetaAt(u, v) : guidance.ThetaAt(u, v);
                    double between = Between(axis, new PointD(Math.Cos(theta), Math.Sin(theta)));
                    disagreements.Add(between);

                    if (field.EdgeAt(u, v) > 0.25)
                    {
                        onEdge++;
                        if (between > 45.0)
                        {
                            crossing++;
                        }
                    }
                }
            }
        }

        int[] lengths = [.. byCourse.Values.Where(c => c[0].CourseId >= 0).Select(c => c.Length).Order()];

        return new Shape
        {
            AreaMin = areas[0],
            AreaP5 = Percentile(areas, 0.05),
            AreaMedian = Percentile(areas, 0.50),
            AreaP95 = Percentile(areas, 0.95),
            AreaMax = areas[^1],
            TinyShare = (double)areas.Count(a => a < 0.4) / areas.Length,
            SliverShare = (double)slivers / tesserae.Count,
            ManySidedShare = (double)manySided / tesserae.Count,
            KinkShare = joints > 0 ? (double)kinks / joints : 0.0,
            CourseCount = lengths.Length,
            FillerShare = (double)tesserae.Count(t => t.CourseId < 0) / tesserae.Count,
            StubCourseShare = lengths.Length == 0 ? 0.0 : (double)lengths.Count(l => l < 3) / lengths.Length,
            MedianCourseLength = lengths.Length == 0 ? 0.0 : lengths[lengths.Length / 2],
            StructureDisagreement = disagreements.Count == 0
                ? 0.0
                : Percentile([.. disagreements.Order()], 0.5),
            EdgesCrossed = onEdge == 0 ? 0.0 : (double)crossing / onEdge,
            MinSideP5 = Percentile([.. minSides.Order()], 0.05),
            UncuttableShare = (double)minSides.Count(m => m < 3.0) / minSides.Count,
            AwkwardShare = (double)minSides.Count(m => m < 5.0) / minSides.Count,
        };
    }

    private static PointD CourseAxis(Tessera[] course, int i)
    {
        if (course.Length > 1)
        {
            int a = Math.Max(0, i - 1);
            int b = Math.Min(course.Length - 1, i + 1);
            if (a != b)
            {
                PointD d = Unit(Sub(course[b].Centroid, course[a].Centroid));
                if ((d.X * d.X) + (d.Y * d.Y) > 0.5)
                {
                    return d;
                }
            }
        }

        // A course of one: take the polygon's longest edge as its direction.
        PointD[] p = course[i].Polygon;
        double best = -1.0;
        PointD axis = new(1.0, 0.0);
        for (int k = 0; k < p.Length; k++)
        {
            PointD e = Sub(p[(k + 1) % p.Length], p[k]);
            double len = (e.X * e.X) + (e.Y * e.Y);
            if (len > best)
            {
                best = len;
                axis = Unit(e);
            }
        }

        return axis;
    }

    private static (double Along, double Across) Extent(PointD[] polygon, PointD axis)
    {
        double alongMin = double.MaxValue, alongMax = double.MinValue;
        double acrossMin = double.MaxValue, acrossMax = double.MinValue;
        foreach (PointD p in polygon)
        {
            double along = (p.X * axis.X) + (p.Y * axis.Y);
            double across = (p.X * -axis.Y) + (p.Y * axis.X);
            alongMin = Math.Min(alongMin, along);
            alongMax = Math.Max(alongMax, along);
            acrossMin = Math.Min(acrossMin, across);
            acrossMax = Math.Max(acrossMax, across);
        }

        return (Math.Max(1e-6, alongMax - alongMin), Math.Max(1e-6, acrossMax - acrossMin));
    }

    /// <summary>Angle between two orientations, in degrees, 0 to 90 — direction has no sign here.</summary>
    private static double Between(PointD a, PointD b)
    {
        double dot = Math.Abs((a.X * b.X) + (a.Y * b.Y));
        return Math.Acos(Math.Clamp(dot, 0.0, 1.0)) * 180.0 / Math.PI;
    }

    private static PointD Sub(PointD a, PointD b) => new(a.X - b.X, a.Y - b.Y);

    private static PointD Unit(PointD v)
    {
        double len = Math.Sqrt((v.X * v.X) + (v.Y * v.Y));
        return len < 1e-9 ? new PointD(0.0, 0.0) : new PointD(v.X / len, v.Y / len);
    }

    private static double Percentile(double[] sorted, double fraction) =>
        sorted[Math.Clamp((int)(fraction * (sorted.Length - 1)), 0, sorted.Length - 1)];

    internal sealed record Colour
    {
        public required double DeltaEMean { get; init; }
        public required double DeltaEP95 { get; init; }
        public required double DeltaEMax { get; init; }
        public required int ColorsUsed { get; init; }

        /// <summary>Shades carrying fewer than five tesserae — a separate article for nothing.</summary>
        public required int RareColors { get; init; }

        /// <summary>Share of the panel taken by its single most-used shade.</summary>
        public required double DominantShare { get; init; }

        /// <summary>ΔE between the lightest shade used and the second lightest — the subject/ground separation.</summary>
        public required double LightestGap { get; init; }

        /// <summary>
        /// Neighbouring tesserae the photograph gives all but the same colour (ΔE &lt; 2) but the
        /// palette gives visibly different ones (ΔE &gt; 8). This is the quantisation cliff: a smooth
        /// sky comes out as hard-edged blotches, which no amount of andamento can rescue.
        /// </summary>
        public required double BandingShare { get; init; }

        /// <summary>
        /// The opposite loss to <see cref="BandingShare"/>: neighbouring tesserae the photograph
        /// separated by more than half a tonal step, laid in one and the same shade. This is
        /// gradation the picture held and the work threw away — a hazy ridge arriving as one flat
        /// band of sky colour. Read it alongside banding, never on its own: driving either to zero
        /// on its own is trivial and ruins the other.
        /// </summary>
        public required double MergedShare { get; init; }
    }

    public static Colour Colours(
        MosaicLayout layout,
        IReadOnlyList<Tessera> tesserae,
        IReadOnlyList<LinearRgb> cells,
        IReadOnlyList<int> indices,
        Palette palette,
        IReadOnlyList<CieLab> observed,
        int shadeCount)
    {
        var deltas = new double[cells.Count];
        var counts = new Dictionary<int, int>();
        for (int i = 0; i < cells.Count; i++)
        {
            deltas[i] = ColorDistance.CieDe76(cells[i].ToLab(), observed[indices[i]]);
            counts[indices[i]] = counts.GetValueOrDefault(indices[i]) + 1;
        }

        Array.Sort(deltas);

        double[] usedL = [.. counts.Keys.Select(k => observed[k].L).OrderDescending()];
        (double Banding, double Merged) pairs = NeighbourLosses(
            layout, tesserae, cells, indices, observed, ToneStep(observed, shadeCount));

        return new Colour
        {
            DeltaEMean = deltas.Average(),
            DeltaEP95 = Percentile(deltas, 0.95),
            DeltaEMax = deltas[^1],
            ColorsUsed = counts.Count,
            RareColors = counts.Values.Count(c => c < 5),
            DominantShare = (double)counts.Values.Max() / cells.Count,
            LightestGap = usedL.Length > 1 ? usedL[0] - usedL[1] : 0.0,
            BandingShare = pairs.Banding,
            MergedShare = pairs.Merged,
        };
    }

    /// <summary>
    /// The tonal step of a work in <paramref name="shadeCount"/> shades: the range the material
    /// covers, divided by how many shades are allowed. Second and ninety-eighth percentiles rather
    /// than the extremes, matching <c>ToneMap</c>, so both speak of the same range.
    /// </summary>
    private static double ToneStep(IReadOnlyList<CieLab> observed, int shadeCount)
    {
        double[] sorted = [.. observed.Select(c => c.L).Order()];
        double low = sorted[Math.Clamp((int)Math.Round(0.02 * (sorted.Length - 1)), 0, sorted.Length - 1)];
        double high = sorted[Math.Clamp((int)Math.Round(0.98 * (sorted.Length - 1)), 0, sorted.Length - 1)];
        return (high - low) / Math.Max(1, shadeCount);
    }

    /// <summary>
    /// Both neighbour losses in one pass over the same pairs: the cliff (photograph agrees, work
    /// disagrees) and the merge (photograph disagrees, work agrees). One bucket grid, one radius,
    /// so the two numbers are always about the same pairs.
    /// </summary>
    private static (double Banding, double Merged) NeighbourLosses(
        MosaicLayout layout,
        IReadOnlyList<Tessera> tesserae,
        IReadOnlyList<LinearRgb> cells,
        IReadOnlyList<int> indices,
        IReadOnlyList<CieLab> observed,
        double toneStep)
    {
        double reach = layout.StepXMm * 1.6;
        var buckets = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < tesserae.Count; i++)
        {
            var key = ((int)(tesserae[i].Centroid.X / reach), (int)(tesserae[i].Centroid.Y / reach));
            if (!buckets.TryGetValue(key, out List<int>? list))
            {
                buckets[key] = list = [];
            }

            list.Add(i);
        }

        var cellLab = new CieLab[cells.Count];
        for (int i = 0; i < cells.Count; i++)
        {
            cellLab[i] = cells[i].ToLab();
        }

        int flatPairs = 0;
        int cliffs = 0;
        int steepPairs = 0;
        int merges = 0;
        double half = toneStep / 2.0;
        double reachSq = reach * reach;

        for (int i = 0; i < tesserae.Count; i++)
        {
            (int bx, int by) = ((int)(tesserae[i].Centroid.X / reach), (int)(tesserae[i].Centroid.Y / reach));
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!buckets.TryGetValue((bx + dx, by + dy), out List<int>? list))
                    {
                        continue;
                    }

                    foreach (int j in list)
                    {
                        if (j <= i)
                        {
                            continue;
                        }

                        double ddx = tesserae[i].Centroid.X - tesserae[j].Centroid.X;
                        double ddy = tesserae[i].Centroid.Y - tesserae[j].Centroid.Y;
                        if ((ddx * ddx) + (ddy * ddy) > reachSq)
                        {
                            continue;
                        }

                        double photo = ColorDistance.CieDe76(cellLab[i], cellLab[j]);

                        if (photo < 2.0)
                        {
                            flatPairs++;
                            if (ColorDistance.CieDe76(observed[indices[i]], observed[indices[j]]) > 8.0)
                            {
                                cliffs++;
                            }
                        }

                        if (photo > half)
                        {
                            steepPairs++;
                            if (indices[i] == indices[j])
                            {
                                merges++;
                            }
                        }
                    }
                }
            }
        }

        return (
            flatPairs > 0 ? (double)cliffs / flatPairs : 0.0,
            steepPairs > 0 ? (double)merges / steepPairs : 0.0);
    }
}
