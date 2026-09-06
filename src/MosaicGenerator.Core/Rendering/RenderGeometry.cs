using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Grid;

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

        int pixelWidth = Math.Max(1, (int)Math.Round(layout.PanelWidthMm * pixelsPerMm));
        int pixelHeight = Math.Max(1, (int)Math.Round(layout.PanelHeightMm * pixelsPerMm));

        double originX = layout.MarginXMm * pixelsPerMm;
        double originY = layout.MarginYMm * pixelsPerMm;

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

            int colorIndex = plan.ColorIndices[i];
            CieLab lab = plan.Palette.Colors[colorIndex].Lab;

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
                Quad = nominal,
                FillColor = lab.ToRgb().Clamped(),
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
            JointColor = options.JointColor,
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
}
