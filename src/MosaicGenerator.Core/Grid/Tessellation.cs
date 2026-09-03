using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Rendering;

namespace MosaicGenerator.Core.Grid;

/// <summary>
/// Lays the tesserae out over the field.
///
/// The followed layout is one connected pass, in the order a mosaicist works. First the border: two
/// straight courses along the panel edge, which hold the composition and stop the fill from running
/// off the edge. Then the contours — the subject's silhouette and
/// its major internal edges — each get two tight courses hugging them (opus vermiculatum). Then
/// the rest of the field is filled with evenly-spaced streamlines (Jobard &amp; Lefèvre 1997) over a
/// guidance field that echoes the nearest contour where the photograph is flat and follows the
/// photograph's own texture where it is not (opus musivum). A streamline can arc all the way around
/// a form; nothing is sprung back to a horizontal band, and the spacing check keeps courses from
/// crossing or leaving gaps.
/// </summary>
public static class Tessellation
{
    /// <summary>
    /// Diagnostic only: why streamlines stopped, in the order
    /// {out of field, spacing, self-intersection, curvature, step budget}. Reset by the bench
    /// before a run and read after it; nothing in the pipeline looks at it.
    /// </summary>
    public static readonly int[] BreakReasons = new int[5];

    /// <summary>Plain rectangular grid in field millimetres — the fallback when there is no photograph to follow.</summary>
    public static IReadOnlyList<Tessera> NominalGrid(MosaicLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        double fieldWidth = layout.FieldWidthMm;
        double fieldHeight = layout.FieldHeightMm;
        double module = layout.ModuleWidthMm;
        double moduleHeight = layout.ModuleHeightMm;
        double stepX = layout.StepXMm;
        double stepY = layout.StepYMm;
        double fullArea = module * moduleHeight;

        var tesserae = new List<Tessera>(layout.TotalModules);
        for (int row = 0; row < layout.Rows; row++)
        {
            double top = row * stepY;
            for (int column = 0; column < layout.Columns; column++)
            {
                double left = column * stepX;
                PointD[] nominal =
                [
                    new(left, top),
                    new(left + module, top),
                    new(left + module, top + moduleHeight),
                    new(left, top + moduleHeight),
                ];

                tesserae.Add(Finish(nominal, fieldWidth, fieldHeight, fullArea, row, column));
            }
        }

        return tesserae;
    }

    public static IReadOnlyList<Tessera> Advected(MosaicLayout layout, DirectionField field)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(field);

        double fieldWidth = layout.FieldWidthMm;
        double fieldHeight = layout.FieldHeightMm;
        double grout = layout.GroutWidthMm;

        // The piece is a rectangle and its two sides mean different things. Along the course runs the
        // length of the bite, which the mosaicist chooses; across the course sits the thickness of the
        // plate, because smalt is set cut-face up and the fracture runs through the plate. So the
        // layout needs two spacings, not one: courses sit a plate apart, pieces sit a bite apart
        // along them. A single spacing forced every piece square and fought the andamento — a run of
        // elongated pieces is what makes a course read as a line of the form rather than as a grid.
        double along = layout.ModuleWidthMm;
        double across = layout.ModuleHeightMm;
        double module = Math.Min(along, across);   // the piece's narrow side, for scale-relative limits

        // How much longer than the base bite a course may run where the picture is calm. A mosaicist
        // bites longer pieces on an open background and shorter ones where the form turns; across the
        // course the plate fixes the width, so only this one length varies, and it varies between
        // courses, never within one. Twice the base takes a 6-8 mm module to 12-16 mm on the open
        // background — the size a real mosaic uses there — while the snap to the size series keeps it
        // to two or three deliberate steps.
        const double MaxAlongScale = 2.0;

        // Floor on a piece that can actually be broken and placed. It is the plate, not the bite:
        // across the course a piece is exactly as wide as the plate is thick — that side cannot be
        // knapped narrower — so a cell squeezed well under it has no piece that would fit. Along the
        // course the bite is free, but a chip much shorter than the plate is one of those accidents
        // that happen and cannot be worked with. The tolerance is six tenths of the plate rather than
        // a half because the courses now run closer to each other than they are seeded: the slack
        // that buys long runs is paid for here, by not laying the slivers it squeezes out.
        double floorAcross = across * 0.6;

        double dAlong = along + grout;
        double dAcross = across + grout;
        double dSeed = 0.82 * dAcross;   // an offset seed survives even where the parent course curves
        // A streamline is seeded a course apart, but it is allowed to run closer than that before it
        // gives up. Jobard and Lefevre make these two distances different for exactly this reason: a
        // line that stops at its seeding distance brakes against every neighbour it passes, and the
        // field fills with stubs instead of runs. The spacing check is now 98-100% of every course
        // break measured — the curvature limit and self-intersection barely fire. What the slack
        // packs tighter than a piece the knapping and the sliver merge take back out.
        double dTest = 0.50 * dAcross;
        double integStep = Math.Max(0.3, module / 3.0);
        double minLength = 2.0 * dAlong;
        int maxSteps = (int)((fieldWidth + fieldHeight) / integStep) + 32;

        IReadOnlyList<PointD[]> contours = ContourSet.Extract(field, fieldWidth, fieldHeight, along);

        CourseGuidance guidance = CourseGuidance.Build(
            field, fieldWidth, fieldHeight, contours, along, dAcross);

        // Where a course sits relative to the picture's edges decides how long its pieces are cut. The
        // reference is the same level ContourSet draws its contours at — a course running through edge
        // this strong is on a form and stays at the base bite; one through near-flat field is on the
        // background and its bite grows toward MaxAlongScale. Tied to the contour level so there is no
        // separate number to tune. With no contour anywhere there is no background to tell from a
        // subject, so the bite stays uniform.
        double edgeRef = ContourLevel(field);
        bool variableAlong = contours.Count > 0;

        // A tessera is a rigid piece of glass: a course cannot bend tighter than about two and a half
        // times its own width. Where the guidance field turns faster than that — the seam between two
        // echo zones, the medial axis inside a form — the course simply ends, as it does in real work,
        // instead of curling into a spiral. What it leaves bare is grown back in step 5.
        // A course cannot bend tighter than about two and a half times its own width, and its width
        // is the plate, not the bite.
        double maxTurn = integStep / (2.5 * dAcross);

        // How far the course may go on turning at that limit before it counts as a real spiral rather
        // than a ripple in the field. A quarter turn: a run rounds the top of a head or the shoulder
        // of a wave and comes back out, but one that holds the tightest bend the glass allows for
        // longer than that is closing on itself. Kept as an angle, so it means the same thing at
        // every panel size.
        int clampBudget = Math.Max(2, (int)(Math.PI / 2.0 / maxTurn));

        var placer = new StreamlinePlacer(
            guidance.ThetaAt, fieldWidth, fieldHeight, dAcross, dSeed, dTest, integStep, maxSteps,
            maxTurn, clampBudget);
        int nextCourseId = 0;

        // A piece grown into a bare spot in step 5 is not a course — it is the single tessera a
        // mosaicist slips in where the runs did not quite meet. It still needs an identity of its
        // own: without one every such piece answers to the same number, which makes them read as one
        // long course wherever the layout is measured, and hands the renderer the same chipping seed
        // twice. Negative numbers say "not a course" and stay distinct.
        int nextFillerId = -1;

        var courses = new List<PointD[]>();

        // 1. The border (bordura): two straight courses along the panel edge.
        PointD[]? innermostRing = null;
        foreach (PointD[] ring in BorderRings(fieldWidth, fieldHeight, dAcross))
        {
            courses.Add(ring);
            placer.RegisterBarrier(ring, integStep);
            innermostRing = ring;
        }

        if (innermostRing is not null)
        {
            placer.SeedAlong(innermostRing, dAcross);   // the field is filled inwards from the border
        }

        // 2. Contour courses (opus vermiculatum): two rows straddling each contour polyline.
        foreach (PointD[] contour in contours)
        {
            foreach (double offset in stackalloc[] { -dAcross / 2.0, dAcross / 2.0 })
            {
                PointD[] row = OffsetPolyline(contour, offset);
                courses.Add(row);
                placer.RegisterBarrier(row, integStep);
            }

            placer.SeedAlong(contour, dAcross);
        }

        // The border and the contour courses are structural — they hold the composition and hug the
        // form — and are cut at the base bite regardless of where they run. Only the fill that comes
        // next takes a longer bite on the calm background.
        int structuralCourses = courses.Count;

        // 3. Fill the rest with evenly-spaced streamlines.
        foreach (List<PointD> course in placer.Place(minLength))
        {
            courses.Add([.. course]);
        }

        // 4. Cut the tesserae. Each course is sampled at the course spacing, and every sample becomes
        //    a piece knapped to the space its neighbours leave it — its Voronoi cell, held back by half
        //    a joint on every side. Laying fixed rectangles and then stamping chips into what they
        //    missed could not work: the chips rode over their neighbours, and where the rectangles did
        //    meet there was no joint at all. Smalt is never set that way. A cell cannot overlap another
        //    cell, cannot leave a gap, and always leaves the joint.
        var sites = new List<(PointD Centre, PointD Tangent, double AlongMm, int CourseId, int Index)>();
        for (int c = 0; c < courses.Count; c++)
        {
            AddSites(courses[c], sites, resize: variableAlong && c >= structuralCourses);
        }

        // 5. Two corrections, alternating until they settle.
        //
        //    Merge away what cannot be cut: a cell squeezed by its neighbours into a sliver is not a
        //    tessera anyone could break from a plate, and in real work the mosaicist simply does not
        //    place it — the neighbouring piece runs on instead. Dropping the site does exactly that,
        //    because the Voronoi cells around it grow into the space by themselves, with no gap and
        //    no overlap. That is why this is a merge and not a deletion.
        //
        //    Then grow into whatever is still bare: first a course of its own where a whole one fits,
        //    then single pieces. A course that ended at a seam leaves a wedge behind it, and a
        //    mosaicist fills that wedge rather than leaving the adhesive showing. The two have to run
        //    together: a merge opens a little space, and the growth is what closes it.
        var producedBy = new List<int>();
        var tesserae = CutCells(sites, producedBy);
        for (int pass = 0; pass < 4; pass++)
        {
            int merged = MergeSlivers();
            if (merged > 0)
            {
                tesserae = CutCells(sites, producedBy);
            }

            var mask = new CoverageMask(fieldWidth, fieldHeight, module / 6.0);
            foreach (Tessera laid in tesserae)
            {
                mask.Mark(laid.Polygon);
            }

            IReadOnlyList<(PointD Point, double RadiusMm)> holes = mask.BarePoints(module * 0.3);
            if (holes.Count == 0)
            {
                break;
            }

            int added = 0;
            foreach ((PointD hole, double _) in holes)
            {
                if (mask.IsCovered(hole) || TooCloseToASite(hole, sites, dAcross * 0.7))
                {
                    continue;
                }

                if (pass == 0)
                {
                    List<PointD> grown = placer.Trace(hole, dAcross * 0.5);
                    if (PolylineLength(grown) >= minLength * 0.5)
                    {
                        placer.RegisterBarrier(grown, integStep);
                        AddSites([.. grown], sites, resize: false);
                        added++;
                        continue;
                    }
                }

                double theta = guidance.ThetaAt(hole.X / fieldWidth, hole.Y / fieldHeight);
                sites.Add((hole, new PointD(Math.Cos(theta), Math.Sin(theta)), along, nextFillerId--, 0));
                added++;
            }

            if (added == 0 && merged == 0)
            {
                break;
            }

            tesserae = CutCells(sites, producedBy);
        }

        MergeSlivers();
        tesserae = CutCells(sites);

        // Drops every site whose cell came out too small to break from a plate, and reports how many
        // went.
        //
        int MergeSlivers()
        {
            var doomed = new List<int>();
            for (int i = 0; i < tesserae.Count; i++)
            {
                var site = sites[producedBy[i]];
                (double extentAlong, double extentAcross) = Extents(tesserae[i].Polygon, site.Tangent);
                double siteFloorAlong = Math.Max(site.AlongMm * 0.5, floorAcross);
                if (extentAlong < siteFloorAlong || extentAcross < floorAcross)
                {
                    doomed.Add(producedBy[i]);
                }
            }

            doomed.Sort();
            for (int i = doomed.Count - 1; i >= 0; i--)
            {
                sites.RemoveAt(doomed[i]);
            }

            return doomed.Count;
        }

        void AddSites(
            PointD[] course,
            List<(PointD Centre, PointD Tangent, double AlongMm, int CourseId, int Index)> into,
            bool resize)
        {
            double length = PolylineLength(course);
            int courseId = nextCourseId++;

            // The bite length for this whole course: the base module against a form, longer on the
            // calm background. Constant along the course — a row of pieces has to read as one width.
            double courseAlong = resize ? ResizedAlong(course) : along;
            double dAlongLocal = courseAlong + grout;
            double halfLocal = courseAlong * 0.6;

            // Spaced evenly over the course rather than stepped from one end: a fixed step leaves a
            // ragged tail wherever the course does not divide evenly, and on a closed course — the
            // border ring — it left a two-piece gap at the corner where the ring begins and ends.
            bool closed = Distance(course[0], course[^1]) < 1e-6;
            if (closed)
            {
                int n = Math.Max(1, (int)Math.Round(length / dAlongLocal));
                for (int i = 0; i < n; i++)
                {
                    (PointD centre, PointD tangent) = SampleAlong(course, i * length / n);
                    into.Add((centre, tangent, courseAlong, courseId, i));
                }

                return;
            }

            double usable = length - (2.0 * halfLocal);
            if (usable <= 0.0)
            {
                (PointD only, PointD heading) = SampleAlong(course, length / 2.0);
                into.Add((only, heading, courseAlong, courseId, 0));
                return;
            }

            int count = Math.Max(2, (int)Math.Round(usable / dAlongLocal) + 1);
            for (int i = 0; i < count; i++)
            {
                (PointD centre, PointD tangent) = SampleAlong(course, halfLocal + (i * usable / (count - 1)));
                into.Add((centre, tangent, courseAlong, courseId, i));
            }
        }

        // Mean edge strength under the course sets the bite: field this strong is a form and holds the
        // base module, near-flat field is background and the bite grows toward MaxAlongScale. The
        // result snaps to the real size series, so a background reads as two or three deliberate sizes
        // rather than a smear of arbitrary lengths.
        double ResizedAlong(PointD[] course)
        {
            double sum = 0.0;
            foreach (PointD p in course)
            {
                sum += field.EdgeAt(p.X / fieldWidth, p.Y / fieldHeight);
            }

            // Edge under the course: at the contour level the bite stays at the base, and it reaches
            // the full stretch once the course is running through field only about a third that
            // strong — most of an open sky or a calm water.
            double e = course.Length > 0 ? sum / course.Length : edgeRef;
            double t = Math.Clamp(e / Math.Max(edgeRef * 0.35, 1e-6), 0.0, 1.0);
            double scale = MaxAlongScale - ((MaxAlongScale - 1.0) * t);

            // And only where the course itself runs reasonably straight. A long rigid piece cannot
            // follow a bend, so a course that curves hard — tight around a shoulder, into a corner of
            // the field — keeps the base bite even on flat ground. Chord over arc length: 1 is
            // straight, full stretch by about 0.95 (a whole-course bend near 30 degrees), nothing
            // below 0.80.
            double arc = PolylineLength(course);
            double chord = Distance(course[0], course[^1]);
            double straight = arc > 1e-6 ? Math.Clamp((chord / arc - 0.80) / 0.15, 0.0, 1.0) : 0.0;
            scale = 1.0 + ((scale - 1.0) * straight);

            return SnapAlong(along * scale, along, along * MaxAlongScale);
        }

        List<Tessera> CutCells(
            List<(PointD Centre, PointD Tangent, double AlongMm, int CourseId, int Index)> all,
            List<int>? producedBy = null)
        {
            producedBy?.Clear();
            // A site only ever borders another within a couple of modules, so the bisectors are taken
            // against the neighbours in a bucket of that size rather than against all of them. The
            // longest bite the background can take sets the bucket.
            double reach = Math.Max(along * MaxAlongScale, across) * 2.0;
            var buckets = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < all.Count; i++)
            {
                var key = ((int)Math.Floor(all[i].Centre.X / reach), (int)Math.Floor(all[i].Centre.Y / reach));
                (buckets.TryGetValue(key, out List<int>? bucket) ? bucket : buckets[key] = []).Add(i);
            }

            // The starting shape bounds a cell that has no neighbour on some side — an end of a course.
            // Without it such a piece would run out to the far side of the panel. Along the course the
            // bound is the piece's own bite, which varies from course to course.
            double boundAcross = across * 0.95;
            double joint = grout * 0.5;

            var cut = new List<Tessera>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                (PointD centre, PointD tangent, double alongMm, int courseId, int index) = all[i];
                double boundAlong = alongMm * 0.95;
                PointD[] cell = OrientedRectPolygon(
                    centre.X, centre.Y, tangent.X, tangent.Y, boundAlong, boundAcross, boundAcross);

                int cx = (int)Math.Floor(centre.X / reach);
                int cy = (int)Math.Floor(centre.Y / reach);
                for (int gx = cx - 1; gx <= cx + 1; gx++)
                {
                    for (int gy = cy - 1; gy <= cy + 1; gy++)
                    {
                        if (!buckets.TryGetValue((gx, gy), out List<int>? bucket))
                        {
                            continue;
                        }

                        foreach (int j in bucket)
                        {
                            if (j == i)
                            {
                                continue;
                            }

                            PointD other = all[j].Centre;
                            double dx = other.X - centre.X;
                            double dy = other.Y - centre.Y;
                            double across = (dx * -tangent.Y) + (dy * tangent.X);
                            double span = Math.Sqrt((dx * dx) + (dy * dy));

                            // A neighbouring course is cut off by a straight line running along this
                            // course, not by the bisector between two staggered pieces: the mosaicist
                            // runs a line and knaps to it, which is why smalt comes out four-sided.
                            // Bisecting instead turned a staggered field into a honeycomb.
                            cell = !SameRun(all[j].CourseId, courseId) && Math.Abs(across) > span * 0.6
                                ? FieldGeometry.ClipToLine(
                                    cell, centre,
                                    Math.Sign(across) * -tangent.Y, Math.Sign(across) * tangent.X,
                                    (Math.Abs(across) / 2.0) - joint)
                                : FieldGeometry.ClipToBisector(cell, centre, other, joint);
                            if (cell.Length < 3)
                            {
                                break;
                            }
                        }
                    }
                }

                if (cell.Length >= 3)
                {
                    cut.Add(Finish(cell, fieldWidth, fieldHeight, alongMm * across, courseId, index));
                    producedBy?.Add(i);
                }
            }

            return cut;
        }

        // Whether two sites belong to the same run for the purposes of knapping. Two pieces of one
        // course are staggered against each other and meet on the bisector; a piece of a neighbouring
        // course is cut off by a straight line. Fillers have no course, so no line runs between two
        // of them either — they knap against each other like course-mates, which is what they did
        // when they all shared the one identity.
        static bool SameRun(int a, int b) => a == b || (a < 0 && b < 0);

        static bool TooCloseToASite(
            PointD p,
            List<(PointD Centre, PointD Tangent, double AlongMm, int CourseId, int Index)> all,
            double distance)
        {
            double limit = distance * distance;
            foreach ((PointD centre, PointD _, double _, int _, int _) in all)
            {
                double dx = centre.X - p.X;
                double dy = centre.Y - p.Y;
                if ((dx * dx) + (dy * dy) < limit)
                {
                    return true;
                }
            }

            return false;
        }


        return tesserae.Count > 0 ? tesserae : NominalGrid(layout);
    }

    /// <summary>Evenly-spaced streamline placement over a guidance field.</summary>
    private sealed class StreamlinePlacer(
        Func<double, double, double> guide,
        double fieldWidth,
        double fieldHeight,
        double dSep,
        double dSeed,
        double dTest,
        double integStep,
        int maxSteps,
        double maxTurn,
        int clampBudget)
    {
        private double _dTestActive = dTest;

        private readonly Dictionary<(int, int), List<PointD>> _points = [];
        private readonly Queue<PointD> _seeds = new();

        /// <summary>
        /// A streamline from <paramref name="seed"/> with the spacing check relaxed to
        /// <paramref name="testDistance"/>, so a course can be grown into a gap the ordinary spacing
        /// would refuse.
        /// </summary>
        public List<PointD> Trace(PointD seed, double testDistance)
        {
            double previous = _dTestActive;
            _dTestActive = testDistance;
            try
            {
                return Integrate(seed);
            }
            finally
            {
                _dTestActive = previous;
            }
        }

        public void RegisterBarrier(IReadOnlyList<PointD> polyline, double spacing)
        {
            double length = PolylineLength(polyline);
            for (double s = 0.0; s <= length; s += spacing)
            {
                Add(SampleAlong(polyline, s).Centre);
            }
        }

        public void SeedAlong(IReadOnlyList<PointD> polyline, double step)
        {
            double length = PolylineLength(polyline);
            for (double s = 0.0; s <= length; s += step)
            {
                (PointD centre, PointD tangent) = SampleAlong(polyline, s);
                double nx = -tangent.Y;
                double ny = tangent.X;
                _seeds.Enqueue(new PointD(centre.X + (nx * step), centre.Y + (ny * step)));
                _seeds.Enqueue(new PointD(centre.X - (nx * step), centre.Y - (ny * step)));
            }
        }

        public IReadOnlyList<List<PointD>> Place(double minLength)
        {
            _seeds.Enqueue(new PointD(fieldWidth / 2.0, fieldHeight / 2.0));

            var courses = new List<List<PointD>>();
            Drain(courses, minLength);

            // One sweep for spots the offset seeds missed where the field curved hard.
            for (double y = dSep; y < fieldHeight; y += dSep)
            {
                for (double x = dSep; x < fieldWidth; x += dSep)
                {
                    var p = new PointD(x, y);
                    if (!TooClose(p, dSeed))
                    {
                        _seeds.Enqueue(p);
                    }
                }
            }

            Drain(courses, minLength * 0.5);
            return courses;
        }

        private void Drain(List<List<PointD>> courses, double minLength)
        {
            int guard = 0;
            while (_seeds.Count > 0 && guard++ < 400_000)
            {
                PointD seed = _seeds.Dequeue();
                if (!Inside(seed) || TooClose(seed, dSeed))
                {
                    continue;
                }

                List<PointD> course = Integrate(seed);
                if (ArcLength(course) < minLength)
                {
                    continue;
                }

                double length = ArcLength(course);
                for (double s = 0.0; s <= length; s += integStep)
                {
                    Add(SampleAlong(course, s).Centre);
                }

                courses.Add(course);

                for (double s = 0.0; s <= length; s += dSep)
                {
                    (PointD centre, PointD tangent) = SampleAlong(course, s);
                    double nx = -tangent.Y;
                    double ny = tangent.X;
                    _seeds.Enqueue(new PointD(centre.X + (nx * dSep), centre.Y + (ny * dSep)));
                    _seeds.Enqueue(new PointD(centre.X - (nx * dSep), centre.Y - (ny * dSep)));
                }
            }
        }

        private List<PointD> Integrate(PointD seed)
        {
            double theta = guide(seed.X / fieldWidth, seed.Y / fieldHeight);
            var forward = IntegrateHalf(seed, new PointD(Math.Cos(theta), Math.Sin(theta)));
            var backward = IntegrateHalf(seed, new PointD(-Math.Cos(theta), -Math.Sin(theta)));

            backward.Reverse();
            backward.Add(seed);
            backward.AddRange(forward);
            return backward;
        }

        private List<PointD> IntegrateHalf(PointD start, PointD heading)
        {
            var path = new List<PointD>();
            double px = start.X;
            double py = start.Y;
            double hx = heading.X;
            double hy = heading.Y;
            int clamped = 0;

            for (int i = 0; i < maxSteps; i++)
            {
                (double dx1, double dy1) = Direction(px, py, hx, hy);
                double mx = px + (dx1 * integStep * 0.5);
                double my = py + (dy1 * integStep * 0.5);
                (double dx2, double dy2) = Direction(mx, my, dx1, dy1);

                double nx = px + (dx2 * integStep);
                double ny = py + (dy2 * integStep);

                // The curvature limit bends the step back to what the glass allows rather than ending
                // the course on the spot. A single sample turning too sharply is not a turn — on
                // water it is a ripple, and cutting there is what left the dolphin with courses of
                // four pieces. A turn that stays too tight for a whole course spacing is a different
                // thing: that is the medial axis of a form or the seam between two echo zones, where
                // a mosaicist really does end the run.
                if ((dx2 * hx) + (dy2 * hy) < Math.Cos(maxTurn))
                {
                    if (++clamped > clampBudget)
                    {
                        Interlocked.Increment(ref BreakReasons[3]);
                        break;
                    }

                    double turn = (hx * dy2) - (hy * dx2) >= 0.0 ? maxTurn : -maxTurn;
                    double c = Math.Cos(turn);
                    double s = Math.Sin(turn);
                    (dx2, dy2) = ((hx * c) - (hy * s), (hx * s) + (hy * c));
                    nx = px + (dx2 * integStep);
                    ny = py + (dy2 * integStep);
                }
                else
                {
                    clamped = 0;
                }

                if (nx < 0.0 || nx > fieldWidth || ny < 0.0 || ny > fieldHeight)
                {
                    Interlocked.Increment(ref BreakReasons[0]);
                    break;
                }

                if (TooClose(new PointD(nx, ny), _dTestActive))
                {
                    Interlocked.Increment(ref BreakReasons[1]);
                    break;
                }

                if (SelfIntersects(path, nx, ny))
                {
                    Interlocked.Increment(ref BreakReasons[2]);
                    break;
                }

                path.Add(new PointD(nx, ny));
                px = nx;
                py = ny;
                hx = dx2;
                hy = dy2;

                if (i == maxSteps - 1)
                {
                    Interlocked.Increment(ref BreakReasons[4]);
                }
            }

            return path;
        }

        private (double X, double Y) Direction(double x, double y, double hx, double hy)
        {
            double theta = guide(Math.Clamp(x / fieldWidth, 0.0, 1.0), Math.Clamp(y / fieldHeight, 0.0, 1.0));
            double dx = Math.Cos(theta);
            double dy = Math.Sin(theta);
            if ((dx * hx) + (dy * hy) < 0.0)
            {
                dx = -dx;
                dy = -dy;
            }

            return (dx, dy);
        }

        private bool SelfIntersects(List<PointD> path, double x, double y)
        {
            // Both the look-back and the hit radius are in tesserae, not in millimetres: at a 5 mm
            // module a fixed 2 mm radius fired on an ordinary curve around the subject and cut the
            // course short.
            //
            // The radius is the course spacing, not the piece's narrow side. At 0.6 of a module this
            // check never once fired on any of the three photographs — a curl at course spacing
            // passed straight through the net — which left the curvature limit as the only thing
            // holding spirals off. Now that the limit bends instead of cutting, this is what holds
            // them, so the net has to be the size of the thing it catches.
            int cutoff = path.Count - (int)(dSep / integStep) - 4;
            double hit = dSep * 0.8;
            double hitSq = hit * hit;
            for (int i = 0; i < cutoff; i++)
            {
                double dx = path[i].X - x;
                double dy = path[i].Y - y;
                if ((dx * dx) + (dy * dy) < hitSq)
                {
                    return true;
                }
            }

            return false;
        }

        private bool Inside(PointD p) =>
            p.X >= 0.0 && p.X <= fieldWidth && p.Y >= 0.0 && p.Y <= fieldHeight;

        private void Add(PointD p)
        {
            var cell = ((int)Math.Floor(p.X / dSep), (int)Math.Floor(p.Y / dSep));
            (_points.TryGetValue(cell, out List<PointD>? bucket) ? bucket : _points[cell] = []).Add(p);
        }

        private bool TooClose(PointD p, double d)
        {
            double dSq = d * d;
            int cx = (int)Math.Floor(p.X / dSep);
            int cy = (int)Math.Floor(p.Y / dSep);

            for (int gx = cx - 1; gx <= cx + 1; gx++)
            {
                for (int gy = cy - 1; gy <= cy + 1; gy++)
                {
                    if (!_points.TryGetValue((gx, gy), out List<PointD>? bucket))
                    {
                        continue;
                    }

                    foreach (PointD q in bucket)
                    {
                        double dx = q.X - p.X;
                        double dy = q.Y - p.Y;
                        if ((dx * dx) + (dy * dy) < dSq)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }
    }

    /// <summary>
    /// What the layout actually covers, rasterised from the tessera polygons. Distance to the nearest
    /// course is not the same question: a course is a line, a tessera is a strip of a width that
    /// varies, and where a course ended the strip stops well short of its own centre line. Measuring
    /// the wrong one is what left bare channels of adhesive across the work.
    /// </summary>
    private sealed class CoverageMask
    {
        private readonly bool[] _covered;
        private readonly int _w;
        private readonly int _h;
        private readonly double _cell;

        public CoverageMask(double fieldWidth, double fieldHeight, double cell)
        {
            _cell = cell;
            _w = Math.Max(1, (int)Math.Ceiling(fieldWidth / cell));
            _h = Math.Max(1, (int)Math.Ceiling(fieldHeight / cell));
            _covered = new bool[_w * _h];
        }

        public void Mark(ReadOnlySpan<PointD> polygon)
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (PointD p in polygon)
            {
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
            }

            int x0 = Math.Max(0, (int)Math.Floor(minX / _cell));
            int y0 = Math.Max(0, (int)Math.Floor(minY / _cell));
            int x1 = Math.Min(_w - 1, (int)Math.Ceiling(maxX / _cell));
            int y1 = Math.Min(_h - 1, (int)Math.Ceiling(maxY / _cell));

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int k = (y * _w) + x;
                    if (!_covered[k] && FieldGeometry.Contains(polygon, (x + 0.5) * _cell, (y + 0.5) * _cell))
                    {
                        _covered[k] = true;
                    }
                }
            }
        }

        public bool IsCovered(PointD p)
        {
            int x = Math.Clamp((int)(p.X / _cell), 0, _w - 1);
            int y = Math.Clamp((int)(p.Y / _cell), 0, _h - 1);
            return _covered[(y * _w) + x];
        }

        /// <summary>
        /// Bare points with room for a tessera, widest first: the distance from each to the nearest
        /// covered cell, keeping those at least <paramref name="minRadiusMm"/> clear. A joint is bare
        /// too — this is what tells a joint apart from a hole.
        /// </summary>
        public IReadOnlyList<(PointD Point, double RadiusMm)> BarePoints(double minRadiusMm)
        {
            double[] clearance = Clearance();
            double minCells = minRadiusMm / _cell;

            var found = new List<(double Radius, PointD Point)>();
            for (int y = 0; y < _h; y++)
            {
                for (int x = 0; x < _w; x++)
                {
                    double r = clearance[(y * _w) + x];
                    if (r >= minCells)
                    {
                        found.Add((r, new PointD((x + 0.5) * _cell, (y + 0.5) * _cell)));
                    }
                }
            }

            found.Sort((a, b) =>
            {
                int byRadius = b.Radius.CompareTo(a.Radius);
                if (byRadius != 0)
                {
                    return byRadius;
                }

                int byY = a.Point.Y.CompareTo(b.Point.Y);
                return byY != 0 ? byY : a.Point.X.CompareTo(b.Point.X);
            });

            return [.. found.Select(f => (f.Point, f.Radius * _cell))];
        }

        /// <summary>Chamfer distance from every cell to the nearest covered cell, in cells.</summary>
        private double[] Clearance()
        {
            const double Diag = 1.41421356237;
            double ceiling = _w + _h;
            var d = new double[_covered.Length];
            for (int i = 0; i < d.Length; i++)
            {
                d[i] = _covered[i] ? 0.0 : ceiling;
            }

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

    // -- shared -----------------------------------------------------------------------------------

    /// <summary>
    /// The border courses: closed rectangles inset half a course and one and a half courses from the
    /// panel edge. Laid first and registered as barriers, so the fill stops against them.
    /// </summary>
    private static IEnumerable<PointD[]> BorderRings(double fieldWidth, double fieldHeight, double dSep)
    {
        foreach (double inset in new[] { dSep * 0.5, dSep * 1.5 })
        {
            double right = fieldWidth - inset;
            double bottom = fieldHeight - inset;
            if (right - inset < dSep || bottom - inset < dSep)
            {
                yield break;   // a panel too small to carry a border
            }

            yield return
            [
                new PointD(inset, inset),
                new PointD(right, inset),
                new PointD(right, bottom),
                new PointD(inset, bottom),
                new PointD(inset, inset),
            ];
        }
    }

    private static PointD[] OffsetPolyline(PointD[] polyline, double offset)
    {
        var result = new PointD[polyline.Length];
        for (int i = 0; i < polyline.Length; i++)
        {
            int a = Math.Max(0, i - 1);
            int b = Math.Min(polyline.Length - 1, i + 1);
            double dx = polyline[b].X - polyline[a].X;
            double dy = polyline[b].Y - polyline[a].Y;
            double norm = Math.Sqrt((dx * dx) + (dy * dy));
            if (norm < 1e-9)
            {
                result[i] = polyline[i];
                continue;
            }

            result[i] = new PointD(
                polyline[i].X + (-dy / norm * offset),
                polyline[i].Y + (dx / norm * offset));
        }

        return result;
    }

    private static double PolylineLength(IReadOnlyList<PointD> polyline) =>
        Geometry.PolylineLength(polyline);

    /// <summary>
    /// The edge strength that counts as being on a contour — the same level <see cref="ContourSet"/>
    /// draws its contours at, so the piece-size signal and the contour courses agree on where a form
    /// is. Returns 0 for a photograph with no real edges.
    /// </summary>
    private static double ContourLevel(DirectionField field)
    {
        ReadOnlySpan<double> edge = field.EdgeCells;
        var sorted = edge.ToArray();
        Array.Sort(sorted);
        int index = Math.Clamp((int)(0.99 * (sorted.Length - 1)), 0, sorted.Length - 1);
        double strong = sorted[index];
        return strong < 0.15 ? 0.0 : Math.Max(0.12, strong * 0.42);
    }

    /// <summary>
    /// The nearest bite on the real size series {6, 8, 10, 12, 15, 20} mm to <paramref name="target"/>,
    /// never below <paramref name="floor"/> and never above <paramref name="ceiling"/>. On a tie the
    /// shorter bite wins, as the finer module does in <c>ModuleSelector</c>.
    /// </summary>
    private static double SnapAlong(double target, double floor, double ceiling)
    {
        ReadOnlySpan<double> series = [6.0, 8.0, 10.0, 12.0, 15.0, 20.0];
        double best = floor;
        double bestGap = Math.Abs(target - floor);
        foreach (double v in series)
        {
            if (v < floor - 1e-6 || v > ceiling + 1e-6)
            {
                continue;
            }

            double gap = Math.Abs(target - v);
            if (gap < bestGap - 1e-9)
            {
                best = v;
                bestGap = gap;
            }
        }

        return best;
    }

    /// <summary>The bounded shape a cell starts from, before its neighbours cut it down.</summary>
    /// <summary>Size of an outline along and across its course, in millimetres.</summary>
    private static (double Along, double Across) Extents(PointD[] polygon, PointD tangent)
    {
        double alongMin = double.MaxValue, alongMax = double.MinValue;
        double acrossMin = double.MaxValue, acrossMax = double.MinValue;
        foreach (PointD p in polygon)
        {
            double a = (p.X * tangent.X) + (p.Y * tangent.Y);
            double b = (p.X * -tangent.Y) + (p.Y * tangent.X);
            alongMin = Math.Min(alongMin, a);
            alongMax = Math.Max(alongMax, a);
            acrossMin = Math.Min(acrossMin, b);
            acrossMax = Math.Max(acrossMax, b);
        }

        return (alongMax - alongMin, acrossMax - acrossMin);
    }

    private static PointD[] OrientedRectPolygon(
        double cx, double cy, double tx, double ty, double halfAlong, double halfUp, double halfDown)
    {
        double nx = -ty;
        double ny = tx;
        return
        [
            new PointD(cx - (tx * halfAlong) - (nx * halfDown), cy - (ty * halfAlong) - (ny * halfDown)),
            new PointD(cx + (tx * halfAlong) - (nx * halfDown), cy + (ty * halfAlong) - (ny * halfDown)),
            new PointD(cx + (tx * halfAlong) + (nx * halfUp), cy + (ty * halfAlong) + (ny * halfUp)),
            new PointD(cx - (tx * halfAlong) + (nx * halfUp), cy - (ty * halfAlong) + (ny * halfUp)),
        ];
    }

    private static Tessera Finish(
        PointD[] nominal, double fieldWidth, double fieldHeight, double fullArea, int courseId, int index)
    {
        PointD[] clipped = FieldGeometry.ClipToRect(nominal, 0.0, 0.0, fieldWidth, fieldHeight);
        PointD[] polygon = clipped.Length >= 3 ? clipped : nominal;

        double area = FieldGeometry.Area(polygon);
        return new Tessera
        {
            Polygon = polygon,
            Centroid = FieldGeometry.Centroid(polygon),
            AreaMm2 = area,
            CourseId = courseId,
            IndexInCourse = index,
            IsCut = area < fullArea * 0.995,
        };
    }

    private static double ArcLength(List<PointD> path) => PolylineLength(path);

    private static (PointD Centre, PointD Tangent) SampleAlong(IReadOnlyList<PointD> path, double s)
    {
        if (path.Count == 1)
        {
            return (path[0], new PointD(1.0, 0.0));
        }

        double travelled = 0.0;
        for (int i = 1; i < path.Count; i++)
        {
            double segment = Distance(path[i - 1], path[i]);
            if (travelled + segment >= s || i == path.Count - 1)
            {
                double t = segment < 1e-9 ? 0.0 : Math.Clamp((s - travelled) / segment, 0.0, 1.0);
                PointD a = path[i - 1];
                PointD b = path[i];
                var centre = new PointD(a.X + (t * (b.X - a.X)), a.Y + (t * (b.Y - a.Y)));

                double dx = b.X - a.X;
                double dy = b.Y - a.Y;
                double norm = Math.Sqrt((dx * dx) + (dy * dy));
                PointD tangent = norm < 1e-9 ? new PointD(1.0, 0.0) : new PointD(dx / norm, dy / norm);
                return (centre, tangent);
            }

            travelled += segment;
        }

        return (path[^1], new PointD(1.0, 0.0));
    }

    private static double Distance(PointD a, PointD b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
