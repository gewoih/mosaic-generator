using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Grid;
using MosaicGenerator.Core.Optics;

namespace MosaicGenerator.Core.Rendering;

/// <summary>
/// Turns a finished plan into positioned, coloured shapes. Kept free of any graphics library so
/// the layout can be asserted directly instead of by comparing rendered images.
/// </summary>
public static class RenderGeometry
{
    public static RenderPlan Compute(MosaicPlan plan, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(options);

        MosaicLayout layout = plan.Layout;
        double pixelsPerMm = ResolveScale(layout, options);

        JointOptics joint = JointOptics.For(layout, plan.Palette.TypicalThicknessMm);

        int pixelWidth = Math.Max(1, (int)Math.Round(layout.PanelWidthMm * pixelsPerMm));
        int pixelHeight = Math.Max(1, (int)Math.Round(layout.PanelHeightMm * pixelsPerMm));

        double originX = layout.MarginXMm * pixelsPerMm;
        double originY = layout.MarginYMm * pixelsPerMm;
        double fieldRight = (layout.MarginXMm + layout.FieldWidthMm) * pixelsPerMm;
        double fieldBottom = (layout.MarginYMm + layout.FieldHeightMm) * pixelsPerMm;

        IReadOnlyList<Tessera> tesserae = plan.Tesserae;
        var modules = new List<RenderedModule>(tesserae.Count);
        var areas = new List<double>(tesserae.Count);

        for (int i = 0; i < tesserae.Count; i++)
        {
            Tessera tessera = tesserae[i];

            var nominal = new PointD[tessera.Polygon.Length];
            for (int p = 0; p < nominal.Length; p++)
            {
                nominal[p] = new PointD(
                    originX + (tessera.Polygon[p].X * pixelsPerMm),
                    originY + (tessera.Polygon[p].Y * pixelsPerMm));
            }

            RectD bounds = BoundsOf(nominal);
            areas.Add(bounds.Width * bounds.Height);

            var random = new DeterministicRandom(plan.Seed, tessera.CourseId, tessera.IndexInCourse);
            int colorIndex = plan.ColorIndices[i];
            CieLab lab = plan.Palette.Colors[colorIndex].Lab;

            PointD[] quad = Shape(nominal, options, originX, fieldRight, originY, fieldBottom, ref random);
            (Rgb fill, Rgb low, Rgb high) = FaceColours(lab, options, ref random);

            modules.Add(new RenderedModule
            {
                Row = tessera.CourseId,
                Column = tessera.IndexInCourse,
                ColorIndex = colorIndex,
                IsCut = tessera.IsCut,
                Bounds = bounds,
                Centroid = new PointD(
                    originX + (tessera.Centroid.X * pixelsPerMm),
                    originY + (tessera.Centroid.Y * pixelsPerMm)),
                Quad = quad,
                FillColor = fill,
                GlossLow = low,
                GlossHigh = high,
            });
        }

        areas.Sort();
        double medianArea = areas.Count == 0 ? 0.0 : areas[areas.Count / 2];

        return new RenderPlan
        {
            PixelWidth = pixelWidth,
            PixelHeight = pixelHeight,
            PixelsPerMm = pixelsPerMm,
            Layout = layout,
            Modules = modules,
            TesseraPixels = Math.Sqrt(medianArea),
            JointColor = joint.JointRgb,
        };
    }

    /// <summary>
    /// Starts from the requested pixels per step and pulls it down until the raster fits both
    /// caps, so an oversized panel loses detail instead of stalling the request.
    /// </summary>
    private static double ResolveScale(MosaicLayout layout, RenderOptions options)
    {
        double step = Math.Min(layout.StepXMm, layout.StepYMm);
        double pixelsPerMm = options.PixelsPerStep / step;

        double width = layout.PanelWidthMm * pixelsPerMm;
        double height = layout.PanelHeightMm * pixelsPerMm;

        double limit = 1.0;

        double longSide = Math.Max(width, height);
        if (longSide > options.MaxLongSidePx)
        {
            limit = Math.Min(limit, options.MaxLongSidePx / longSide);
        }

        double total = width * height;
        if (total > options.MaxTotalPixels)
        {
            limit = Math.Min(limit, Math.Sqrt(options.MaxTotalPixels / total));
        }

        // Rounding up to whole pixels can nudge the result back over the cap; shave a hair off.
        return limit < 1.0 ? pixelsPerMm * limit * (1.0 - 1e-9) : pixelsPerMm;
    }

    private static RectD BoundsOf(ReadOnlySpan<PointD> points)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (PointD point in points)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        return new RectD(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// The tessera outline: the nominal shape resized a little about its centre, each vertex
    /// chipped by an independent signed amount, then the whole shape turned off-square. All
    /// amplitudes are fractions of the module, so a coarse panel is not rougher than a fine one.
    /// The draw order is fixed (size, then vertices, then rotation) so a seed reproduces the shape.
    /// </summary>
    private static PointD[] Shape(
        PointD[] nominal,
        RenderOptions options,
        double fieldLeft,
        double fieldRight,
        double fieldTop,
        double fieldBottom,
        ref DeterministicRandom random)
    {
        RectD bounds = BoundsOf(nominal);
        double cx = bounds.X + (bounds.Width / 2.0);
        double cy = bounds.Y + (bounds.Height / 2.0);

        double scale = 1.0 + (random.NextSigned() * options.SizeJitter);
        double roughX = bounds.Width * options.EdgeRoughness;
        double roughY = bounds.Height * options.EdgeRoughness;
        double angle = random.NextSigned() * options.RotationJitterDeg * Math.PI / 180.0;
        double cos = Math.Cos(angle);
        double sin = Math.Sin(angle);

        var shaped = new PointD[nominal.Length];
        for (int i = 0; i < nominal.Length; i++)
        {
            double lx = ((nominal[i].X - cx) * scale) + (random.NextSigned() * roughX);
            double ly = ((nominal[i].Y - cy) * scale) + (random.NextSigned() * roughY);

            double x = cx + (lx * cos) - (ly * sin);
            double y = cy + (lx * sin) + (ly * cos);
            shaped[i] = new PointD(
                Math.Clamp(x, fieldLeft, fieldRight),
                Math.Clamp(y, fieldTop, fieldBottom));
        }

        return shaped;
    }

    /// <summary>
    /// The face's three lightnesses: a mid value with the per-module tone offset, and a lit and a
    /// shaded edge for the gloss ramp. Shifts happen in L* so equal steps look equal across the
    /// palette; a and b are left alone so the tessera still reads as its colour.
    /// </summary>
    private static (Rgb Fill, Rgb Low, Rgb High) FaceColours(
        CieLab lab, RenderOptions options, ref DeterministicRandom random)
    {
        double mid = Math.Clamp(lab.L + (random.NextSigned() * options.ToneJitter), 0.0, 100.0);

        Rgb At(double l) => new CieLab(Math.Clamp(l, 0.0, 100.0), lab.A, lab.B).ToRgb().Clamped();

        return (At(mid), At(mid - options.GlossJitter), At(mid + options.GlossJitter));
    }
}
