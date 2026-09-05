using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Imaging;

namespace MosaicGenerator.Core.Grid;

/// <summary>
/// The direction the courses should run, sampled over the cropped photograph. Rows of tesserae
/// read as a mosaic — rather than as cross-stitch — when they follow the form: parallel to a
/// contour where the image has one, drifting smoothly where it does not.
///
/// The orientation comes from the structure tensor of the luminance: its dominant gradient is
/// across the local edge, so a quarter turn from it runs along the edge. That orientation is then
/// diffused into the flat areas, weighted so a strong edge holds its direction and a featureless
/// patch inherits its neighbours'. With nothing to go on anywhere the field relaxes to horizontal.
/// </summary>
public sealed class DirectionField
{
    // The course orientation as a double-angle unit-ish vector (cos 2ψ, sin 2ψ), so it can be
    // averaged without the ±π wrap. Length carries how sure the field is of that direction.
    private readonly double[] _vx;
    private readonly double[] _vy;

    // Edge strength before diffusion, 0..1, for finding contours to run a course along.
    private readonly double[] _edge;

    // Foreground / background over the grid, or null when there is no clean figure to find.
    private readonly FigureMask? _figure;

    private DirectionField(int width, int height, double[] vx, double[] vy, double[] edge, FigureMask? figure)
    {
        Width = width;
        Height = height;
        _vx = vx;
        _vy = vy;
        _edge = edge;
        _figure = figure;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Raw edge-strength grid, 0..1, row-major, <see cref="Width"/> by <see cref="Height"/> — for contour extraction.</summary>
    internal ReadOnlySpan<double> EdgeCells => _edge;

    /// <summary>Foreground / background over the grid, for drawing the silhouette as a closed ring. Null when there is no clean figure.</summary>
    internal FigureMask? Figure => _figure;

    /// <summary>Structure-tensor orientation and its confidence at a grid cell, for blending with contour guidance.</summary>
    internal (double Theta, double Coherence) TensorAt(double u, double v)
    {
        (double vx, double vy) = SampleVector(u, v);
        return (0.5 * Math.Atan2(vy, vx), Math.Sqrt((vx * vx) + (vy * vy)));
    }

    /// <summary>Course orientation in radians at field-normalised (<paramref name="u"/>, <paramref name="v"/>), each in [0, 1].</summary>
    public double ThetaAt(double u, double v)
    {
        (double vx, double vy) = SampleVector(u, v);
        return 0.5 * Math.Atan2(vy, vx);
    }

    /// <summary>How strongly the field points that way, 0 (featureless) upward.</summary>
    public double CoherenceAt(double u, double v)
    {
        (double vx, double vy) = SampleVector(u, v);
        return Math.Sqrt((vx * vx) + (vy * vy));
    }

    /// <summary>
    /// Whether (<paramref name="u"/>, <paramref name="v"/>) falls inside the figure's silhouette.
    /// False both when the point is on the background and when the photo has no clean figure at
    /// all — for the bench, which needs to tell "inside the silhouette" from "flat background"
    /// without touching <see cref="Figure"/> directly.
    /// </summary>
    public bool IsForeground(double u, double v) => _figure?.ForegroundAt(u, v) ?? false;

    /// <summary>Edge strength at (<paramref name="u"/>, <paramref name="v"/>), 0..1 — where a contour course could run.</summary>
    public double EdgeAt(double u, double v)
    {
        double fx = Math.Clamp(u, 0.0, 1.0) * (Width - 1);
        double fy = Math.Clamp(v, 0.0, 1.0) * (Height - 1);
        int x0 = (int)Math.Floor(fx);
        int y0 = (int)Math.Floor(fy);
        int x1 = Math.Min(x0 + 1, Width - 1);
        int y1 = Math.Min(y0 + 1, Height - 1);
        return Bilerp(_edge, x0, y0, x1, y1, fx - x0, fy - y0);
    }

    /// <summary>
    /// Grid resolution for a layout: about six cells per module. The layout cannot express a direction
    /// that changes more often than once per tessera — a finer field only breaks courses against
    /// detail no piece of glass can follow, and multiplies the medial axes that split the background
    /// into wedges. A 15 cm panel at the draft level used to get twenty cells per module.
    /// </summary>
    public static int ResolutionFor(MosaicLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return Math.Clamp((int)Math.Round(6.0 * Math.Max(layout.Columns, layout.Rows)), 96, 512);
    }

    public static DirectionField Compute(
        SourceImage image, CropRect crop, double fieldAspect, int longSide = 512)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fieldAspect);
        ArgumentOutOfRangeException.ThrowIfLessThan(longSide, 16);

        int width, height;
        if (fieldAspect >= 1.0)
        {
            width = longSide;
            height = Math.Max(16, (int)Math.Round(longSide / fieldAspect));
        }
        else
        {
            height = longSide;
            width = Math.Max(16, (int)Math.Round(longSide * fieldAspect));
        }

        CieLab[] colour = SampleColour(image, crop, width, height);
        var luminance = new double[width * height];
        for (int i = 0; i < luminance.Length; i++)
        {
            luminance[i] = colour[i].L;
        }

        // Which way the picture runs — measured by the shared tensor, so the flattener that
        // steers by it and the courses that follow it read the same numbers.
        StructureTensor tensor = StructureTensor.Compute(luminance, width, height);
        double[] vx = [.. tensor.Vx];
        double[] vy = [.. tensor.Vy];
        double[] edge = [.. tensor.Edge];

        // Separate the subject from the surround while the colour is still to hand: ContourSet draws
        // the silhouette off this, so the ring closes even where the subject matches the surround in
        // tone. Null when there is no clean figure — ContourSet then uses the edge level set alone.
        FigureMask? figure = FigureMask.Build(width, height, colour, edge, ContourSet.LevelFor(edge));

        // A blank interior has nothing to diffuse from, so seed every unsure cell with the mean
        // orientation around the frame. The courses then echo the frame instead of defaulting to
        // horizontal everywhere; contour echoing (opus musivum) is layered on later.
        SeedBlankCells(vx, vy, width, height);

        Diffuse(vx, vy, width, height);
        return new DirectionField(width, height, vx, vy, edge, figure);
    }

    private (double Vx, double Vy) SampleVector(double u, double v)
    {
        double fx = Math.Clamp(u, 0.0, 1.0) * (Width - 1);
        double fy = Math.Clamp(v, 0.0, 1.0) * (Height - 1);

        int x0 = (int)Math.Floor(fx);
        int y0 = (int)Math.Floor(fy);
        int x1 = Math.Min(x0 + 1, Width - 1);
        int y1 = Math.Min(y0 + 1, Height - 1);
        double tx = fx - x0;
        double ty = fy - y0;

        double vx = Bilerp(_vx, x0, y0, x1, y1, tx, ty);
        double vy = Bilerp(_vy, x0, y0, x1, y1, tx, ty);
        return (vx, vy);
    }

    private double Bilerp(double[] field, int x0, int y0, int x1, int y1, double tx, double ty)
    {
        double top = Lerp(field[(y0 * Width) + x0], field[(y0 * Width) + x1], tx);
        double bottom = Lerp(field[(y1 * Width) + x0], field[(y1 * Width) + x1], tx);
        return Lerp(top, bottom, ty);
    }

    private static double Lerp(double a, double b, double t) => a + (t * (b - a));

    private static CieLab[] SampleColour(SourceImage image, CropRect crop, int width, int height)
    {
        var colour = new CieLab[width * height];
        double blockW = (double)crop.Width / width;
        double blockH = (double)crop.Height / height;

        for (int y = 0; y < height; y++)
        {
            int py0 = crop.Y + (int)(y * blockH);
            int py1 = crop.Y + (int)Math.Ceiling((y + 1) * blockH);
            for (int x = 0; x < width; x++)
            {
                int px0 = crop.X + (int)(x * blockW);
                int px1 = crop.X + (int)Math.Ceiling((x + 1) * blockW);
                LinearRgb average = image.AverageLinear(px0, py0, Math.Max(1, px1 - px0), Math.Max(1, py1 - py0));
                colour[(y * width) + x] = average.ToLab();
            }
        }

        return colour;
    }

    /// <summary>Mean orientation of the frame cells, filled into every interior cell the tensor was unsure of.</summary>
    private static void SeedBlankCells(double[] vx, double[] vy, int width, int height)
    {
        double sumX = 0.0, sumY = 0.0;
        int n = 0;
        void Take(int k)
        {
            sumX += vx[k];
            sumY += vy[k];
            n++;
        }

        for (int x = 0; x < width; x++)
        {
            Take(x);
            Take(((height - 1) * width) + x);
        }

        for (int y = 1; y < height - 1; y++)
        {
            Take(y * width);
            Take((y * width) + width - 1);
        }

        double meanLen = Math.Sqrt((sumX * sumX) + (sumY * sumY)) / Math.Max(1, n);
        double meanX = n > 0 && meanLen > 1e-6 ? sumX / n / meanLen : 1.0;
        double meanY = n > 0 && meanLen > 1e-6 ? sumY / n / meanLen : 0.0;

        // A faint hint only: the seeded cells must yield to diffusion from the real edges, so an
        // edge's orientation can flood the whole region around it instead of staying a thin band.
        // Kept well below the confidence real edges now carry (percentile-normalised, ≈1), so the
        // field's coherence stops being a constant plateau at this value.
        const double hint = 0.03;
        for (int k = 0; k < vx.Length; k++)
        {
            if (Math.Sqrt((vx[k] * vx[k]) + (vy[k] * vy[k])) < 0.05)
            {
                vx[k] = meanX * hint;
                vy[k] = meanY * hint;
            }
        }
    }

    /// <summary>
    /// Jacobi relaxation of the orientation field. Each cell moves toward the average of its
    /// neighbours, but the more sure it already is (longer vector) the less it gives way; a faint
    /// pull toward horizontal is the last resort where even the frame had nothing to say.
    /// </summary>
    private static void Diffuse(double[] vx, double[] vy, int width, int height)
    {
        const int iterations = 40;
        const double horizontalBias = 0.0;

        var nx = new double[vx.Length];
        var ny = new double[vy.Length];

        for (int iter = 0; iter < iterations; iter++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int k = (y * width) + x;

                    int xm = Math.Max(0, x - 1);
                    int xp = Math.Min(width - 1, x + 1);
                    int ym = Math.Max(0, y - 1);
                    int yp = Math.Min(height - 1, y + 1);

                    double ax = (vx[(y * width) + xm] + vx[(y * width) + xp]
                        + vx[(ym * width) + x] + vx[(yp * width) + x]) / 4.0;
                    double ay = (vy[(y * width) + xm] + vy[(y * width) + xp]
                        + vy[(ym * width) + x] + vy[(yp * width) + x]) / 4.0;

                    // Confidence: keep more of the current vector where it is already long.
                    double keep = Math.Min(0.93, Math.Sqrt((vx[k] * vx[k]) + (vy[k] * vy[k])));

                    double mixX = (keep * vx[k]) + ((1.0 - keep) * ax);
                    double mixY = (keep * vy[k]) + ((1.0 - keep) * ay);

                    nx[k] = mixX + (horizontalBias * (1.0 - mixX));
                    ny[k] = mixY - (horizontalBias * mixY);
                }
            }

            Array.Copy(nx, vx, vx.Length);
            Array.Copy(ny, vy, vy.Length);
        }
    }
}
