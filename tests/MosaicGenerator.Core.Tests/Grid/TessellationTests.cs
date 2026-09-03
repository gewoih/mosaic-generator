using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Grid;
using MosaicGenerator.Core.Imaging;
using MosaicGenerator.Core.Rendering;
using MosaicGenerator.Core.Tests.Support;

namespace MosaicGenerator.Core.Tests.Grid;

public class TessellationTests
{
    private static readonly MosaicLayout Layout = RequestFactory.Layout(600, 400, module: 6, grout: 1);

    [Fact]
    public void TheNominalGridHasOneTesseraPerCell()
    {
        IReadOnlyList<Tessera> tesserae = Tessellation.NominalGrid(Layout);

        Assert.Equal(Layout.TotalModules, tesserae.Count);
        Assert.All(tesserae, t => Assert.InRange(t.CourseId, 0, Layout.Rows - 1));
        Assert.All(tesserae, t => Assert.InRange(t.IndexInCourse, 0, Layout.Columns - 1));

        // An exact-fit panel: every tessera is a whole module.
        double module = Layout.ModuleWidthMm * Layout.ModuleHeightMm;
        Assert.All(tesserae, t => Assert.Equal(module, t.AreaMm2, 1e-6));
    }

    [Fact]
    public void AdvectedCoversTheFieldWithoutTooManyGaps()
    {
        DirectionField field = Field("#808080");

        IReadOnlyList<Tessera> advected = Tessellation.Advected(Layout, field);

        double covered = advected.Sum(t => t.AreaMm2);
        double fieldArea = Layout.FieldWidthMm * Layout.FieldHeightMm;
        // Roughly one field's worth of tessera: well under 2x (no gross overlap) and over ~0.6x
        // (no bare channels). Strip-fill lets tesserae meet with a slight overlap, which is fine.
        Assert.InRange(covered / fieldArea, 0.6, 1.35);

        // The streamline count is not the grid count, but it should be in the same ballpark:
        // evenly-spaced courses cover the field, and each holds roughly a module's worth per step.
        Assert.InRange(advected.Count, (int)(Layout.TotalModules * 0.5), (int)(Layout.TotalModules * 1.6));
    }

    [Fact]
    public void CoursesFollowAStrongEdgeRatherThanRunningStraightThroughIt()
    {
        // A steep diagonal edge: at least some courses should be tilted well off horizontal,
        // which the old band-locked layout could never do.
        SourceImage image = ImageFactory.FromPixels(
            240, 160, (x, y) => ((double)x / 240) + ((double)y / 160) < 1.0 ? "#101010" : "#F0F0F0");
        DirectionField field = DirectionField.Compute(
            image, new CropRect(0, 0, 240, 160), Layout.FieldAspect);

        IReadOnlyList<Tessera> advected = Tessellation.Advected(Layout, field);

        int steep = advected.Count(t =>
        {
            PointD a = t.Polygon[0];
            PointD b = t.Polygon[1];
            double angle = Math.Atan2(b.Y - a.Y, b.X - a.X);
            return Math.Abs(angle) > 0.5 && Math.Abs(angle) < Math.PI - 0.5;
        });

        Assert.True(steep > advected.Count / 10, $"only {steep} of {advected.Count} tesserae tilt off horizontal");
    }

    [Fact]
    public void EveryTesseraStaysInsideTheField()
    {
        IReadOnlyList<Tessera> advected = Tessellation.Advected(
            Layout, Field("#101010", "#F0F0F0"));

        foreach (Tessera tessera in advected)
        {
            foreach (PointD point in tessera.Polygon)
            {
                Assert.InRange(point.X, -1e-6, Layout.FieldWidthMm + 1e-6);
                Assert.InRange(point.Y, -1e-6, Layout.FieldHeightMm + 1e-6);
            }
        }
    }

    [Fact]
    public void CoursesDoNotOverlap()
    {
        IReadOnlyList<Tessera> advected = Tessellation.Advected(Layout, Field("#808080"));

        // Evenly-spaced streamlines keep a minimum separation, so tessera centroids on different
        // courses should never sit right on top of each other.
        var byCell = new Dictionary<(int, int), int>();
        double cell = Layout.ModuleWidthMm * 0.6;
        int collisions = 0;
        foreach (Tessera t in advected)
        {
            var key = ((int)(t.Centroid.X / cell), (int)(t.Centroid.Y / cell));
            byCell[key] = byCell.GetValueOrDefault(key) + 1;
            if (byCell[key] > 2)
            {
                collisions++;
            }
        }

        Assert.True(collisions < advected.Count / 50, $"{collisions} crowded cells of {advected.Count}");
    }

    [Fact]
    public void TheTessellationIsDeterministic()
    {
        DirectionField field = Field("#202020", "#E0E0E0");

        IReadOnlyList<Tessera> a = Tessellation.Advected(Layout, field);
        IReadOnlyList<Tessera> b = Tessellation.Advected(Layout, field);

        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Centroid, b[i].Centroid);
            Assert.Equal(a[i].AreaMm2, b[i].AreaMm2, 1e-12);
        }
    }

    [Theory]
    [InlineData(600, 400, 6)]
    [InlineData(150, 150, 5)]   // 15x15 cm draft — the coarsest panel the module series allows
    public void TheTesseraeTileTheFieldWithoutOverlapping(double panelWidth, double panelHeight, double module)
    {
        // Three things a wall of smalt must satisfy, and none of them is "as much area as possible":
        // pieces never lie on top of one another, there is a joint between every pair, and there is no
        // bare patch big enough to have held a piece. Summed tessera area answers none of them — it
        // rewards overlap, which is exactly what an earlier layout did, stamping chips over their
        // neighbours to close the gaps.
        MosaicLayout layout = RequestFactory.Layout(panelWidth, panelHeight, module, grout: 1);
        DirectionField field = DirectionField.Compute(
            ImageFactory.FromPixels(
                240, 160,
                (x, y) => (((x - 120.0) * (x - 120.0)) + ((y - 80.0) * (y - 80.0))) < 40.0 * 40.0
                    ? "#101010"
                    : "#F0F0F0"),
            new CropRect(0, 0, 240, 160),
            layout.FieldAspect,
            DirectionField.ResolutionFor(layout));

        CoverageMask mask = CoverageMask.Rasterise(layout, Tessellation.Advected(layout, field));

        Assert.True(
            mask.OverlappedFraction() < 0.001,
            $"{mask.OverlappedFraction():P2} of the field is claimed by two tesserae at once");

        // The joint is the difference between the module and the step, so a field that is fully
        // covered has no joint at all — that is a failure, not a success.
        Assert.InRange(mask.CoveredFraction(), 0.55, 0.80);

        Assert.True(
            mask.BareBeyond(module * 0.3) < 0.015,
            $"{mask.BareBeyond(module * 0.3):P2} of the field is bare by more than a third of a module");
        Assert.True(
            mask.LargestBareRadiusMm() < module * 0.6,
            $"a bare patch of radius {mask.LargestBareRadiusMm():F1} mm (module is {module} mm)");
    }

    [Fact]
    public void FillCoursesRunLargerPiecesOnTheFlatBackgroundThanAgainstTheContour()
    {
        // A mosaicist bites longer pieces where the picture is calm and straight, shorter ones where
        // it turns or carries an edge. A plain horizon — dark band over light — gives straight
        // horizontal courses: those far from the horizon line run into longer pieces than the ones
        // sitting on it.
        DirectionField field = DirectionField.Compute(
            ImageFactory.FromPixels(240, 160, (_, y) => y < 80 ? "#151515" : "#EAEAEA"),
            new CropRect(0, 0, 240, 160),
            Layout.FieldAspect,
            DirectionField.ResolutionFor(Layout));

        IReadOnlyList<Tessera> advected = Tessellation.Advected(Layout, field);

        double horizonY = Layout.FieldHeightMm / 2.0;
        double module = Layout.ModuleWidthMm;
        double grout = Layout.GroutWidthMm;

        var ahead = advected
            .Where(t => t.CourseId >= 0)
            .ToDictionary(t => (t.CourseId, t.IndexInCourse), t => t.Centroid);

        double Step(Tessera t) =>
            ahead.TryGetValue((t.CourseId, t.IndexInCourse + 1), out PointD b)
                ? Math.Sqrt(((b.X - t.Centroid.X) * (b.X - t.Centroid.X))
                    + ((b.Y - t.Centroid.Y) * (b.Y - t.Centroid.Y)))
                : double.NaN;

        var flatGaps = new List<double>();
        var edgeGaps = new List<double>();
        foreach (Tessera t in advected)
        {
            double gap = Step(t);
            if (double.IsNaN(gap) || gap > module * 3.0 || t.CourseId < 0)
            {
                continue;
            }

            double fromHorizon = Math.Abs(t.Centroid.Y - horizonY);
            bool insideBorder = t.Centroid.X > module * 4.0
                && Layout.FieldWidthMm - t.Centroid.X > module * 4.0;
            if (!insideBorder)
            {
                continue;
            }

            if (fromHorizon > Layout.FieldHeightMm * 0.28)
            {
                flatGaps.Add(gap);
            }
            else if (fromHorizon < module * 1.5)
            {
                edgeGaps.Add(gap);
            }
        }

        Assert.True(flatGaps.Count > 20 && edgeGaps.Count > 20,
            $"not enough samples: {flatGaps.Count} flat, {edgeGaps.Count} on the horizon");

        double flat = Median(flatGaps);
        double edge = Median(edgeGaps);
        Assert.True(flat > edge * 1.3,
            $"flat-background pieces ({flat:F1} mm step) are not larger than the ones on the edge ({edge:F1} mm)");

        // The bite lands on the real size row, not an arbitrary length: at least two distinct sizes.
        double[] row = [6, 8, 10, 12, 15, 20];
        int sizes = advected
            .Where(t => t.CourseId >= 0)
            .Select(Step)
            .Where(s => s > 0 && s < module * 3.0)
            .Select(s => row.OrderBy(v => Math.Abs(v - (s - grout))).First())
            .Distinct()
            .Count();
        Assert.True(sizes >= 2, $"only {sizes} distinct bite length in the layout");
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }

    [Fact]
    public void ABorderCourseRunsTheWholeLengthOfEachEdge()
    {
        // The border is laid first, as a mosaicist lays it: a straight course half a course in from
        // the panel edge, unbroken from corner to corner. It is what keeps the field from running off
        // the edge — and what replaced the panel frame acting as a barrier in the distance field.
        IReadOnlyList<Tessera> advected = Tessellation.Advected(Layout, Field("#808080"));

        double dSep = Layout.ModuleWidthMm + (Layout.GroutWidthMm * 0.35);
        double band = Layout.ModuleWidthMm * 0.35;

        Assert.True(
            SpanAlong(advected, t => t.Centroid.Y, t => t.Centroid.X, dSep * 0.5, band, Layout.FieldWidthMm, dSep)
                > 0.9,
            "the top border course does not run the whole width");
        Assert.True(
            SpanAlong(advected, t => t.Centroid.X, t => t.Centroid.Y, dSep * 0.5, band, Layout.FieldHeightMm, dSep)
                > 0.9,
            "the left border course does not run the whole height");
    }

    [Fact]
    public void TheBackgroundEchoesTheSubjectRatherThanThePanel()
    {
        // A dark disc on a light ground. In the corners of the panel — far from the disc, right where
        // the panel edge used to win — the courses still wrap the disc: each runs across the radius
        // from its centre, not along the nearest edge. That is opus musivum. With the frame seeded
        // into the distance field these corners ran flat along the edge, and the background came out
        // as nested rectangles.
        DirectionField field = DirectionField.Compute(
            ImageFactory.FromPixels(
                240, 160,
                (x, y) => (((x - 120.0) * (x - 120.0)) + ((y - 80.0) * (y - 80.0))) < 40.0 * 40.0
                    ? "#101010"
                    : "#F0F0F0"),
            new CropRect(0, 0, 240, 160),
            Layout.FieldAspect);

        IReadOnlyList<Tessera> advected = Tessellation.Advected(Layout, field);

        double cx = Layout.FieldWidthMm / 2.0;
        double cy = Layout.FieldHeightMm / 2.0;
        double dSep = Layout.ModuleWidthMm + (Layout.GroutWidthMm * 0.35);

        var next = advected
            .Where(t => t.CourseId >= 0)
            .ToDictionary(t => (t.CourseId, t.IndexInCourse), t => t.Centroid);

        double sum = 0.0;
        int count = 0;
        foreach (Tessera t in advected)
        {
            double dx = t.Centroid.X - cx;
            double dy = t.Centroid.Y - cy;
            bool inCorner = Math.Abs(dx) > Layout.FieldWidthMm * 0.28
                && Math.Abs(dy) > Layout.FieldHeightMm * 0.28;
            bool insideTheBorder = t.Centroid.X > dSep * 2.5
                && t.Centroid.Y > dSep * 2.5
                && Layout.FieldWidthMm - t.Centroid.X > dSep * 2.5
                && Layout.FieldHeightMm - t.Centroid.Y > dSep * 2.5;
            if (!inCorner || !insideTheBorder)
            {
                continue;
            }

            // The course runs from one piece to the next along it, so that is where its direction is
            // read from — a cell is knapped to its neighbours and its vertices carry no direction.
            if (!next.TryGetValue((t.CourseId, t.IndexInCourse + 1), out PointD ahead))
            {
                continue;
            }

            // How far the course is from perpendicular to the radius, folded to [0, π/2].
            double deviation = Math.Abs(
                NormaliseHalfTurn(
                    Math.Atan2(ahead.Y - t.Centroid.Y, ahead.X - t.Centroid.X)
                    - (Math.Atan2(dy, dx) + (Math.PI / 2.0))));
            sum += deviation;
            count++;
        }

        Assert.True(count > 50, $"only {count} tesserae in the corners");
        Assert.True(sum / count < 0.45, $"corner courses are {sum / count:F2} rad off the disc's echo");
    }

    private static double NormaliseHalfTurn(double theta)
    {
        double t = theta % Math.PI;
        if (t > Math.PI / 2.0)
        {
            t -= Math.PI;
        }
        else if (t <= -Math.PI / 2.0)
        {
            t += Math.PI;
        }

        return t;
    }

    /// <summary>Fraction of <paramref name="extent"/> covered by tesserae sitting in a band at <paramref name="at"/>.</summary>
    private static double SpanAlong(
        IReadOnlyList<Tessera> tesserae, Func<Tessera, double> across, Func<Tessera, double> along,
        double at, double band, double extent, double bucket)
    {
        var buckets = new HashSet<int>();
        foreach (Tessera t in tesserae)
        {
            if (Math.Abs(across(t) - at) <= band)
            {
                buckets.Add((int)(along(t) / bucket));
            }
        }

        return buckets.Count / Math.Ceiling(extent / bucket);
    }

    private static DirectionField Field(string solid) =>
        DirectionField.Compute(
            ImageFactory.Solid(240, 160, solid), new CropRect(0, 0, 240, 160), Layout.FieldAspect);

    private static DirectionField Field(string top, string bottom) =>
        DirectionField.Compute(
            ImageFactory.FromPixels(240, 160, (_, y) => y < 80 ? top : bottom),
            new CropRect(0, 0, 240, 160),
            Layout.FieldAspect);
}
