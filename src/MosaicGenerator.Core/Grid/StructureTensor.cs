namespace MosaicGenerator.Core.Grid;

/// <summary>
/// Which way the picture runs, cell by cell: the structure tensor of the luminance, turned a
/// quarter so it points *along* the local edge rather than across it.
///
/// Two callers need the same measurement for opposite reasons. <see cref="DirectionField"/> reads
/// it to decide where the courses go. <see cref="Imaging.ImageFlattener"/> reads it to decide which
/// way it may blur — along the form, where losing texture costs nothing, and not across it, where
/// the only detail a piece of smalt can carry lives. Computing it twice in two places would be two
/// slightly different measurements pretending to be one.
/// </summary>
public sealed class StructureTensor
{
    private readonly double[] _vx;
    private readonly double[] _vy;
    private readonly double[] _edge;

    private StructureTensor(int width, int height, double[] vx, double[] vy, double[] edge)
    {
        Width = width;
        Height = height;
        _vx = vx;
        _vy = vy;
        _edge = edge;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>
    /// Course orientation as a double-angle vector (cos 2ψ, sin 2ψ), so orientations average
    /// without the ±π wrap. Length is the confidence, 0..1. Row-major, <see cref="Width"/> wide.
    /// </summary>
    public ReadOnlySpan<double> Vx => _vx;

    /// <inheritdoc cref="Vx"/>
    public ReadOnlySpan<double> Vy => _vy;

    /// <summary>Edge strength before any diffusion, 0..1, normalised on the frame maximum.</summary>
    public ReadOnlySpan<double> Edge => _edge;

    /// <summary>Orientation in radians at a grid cell, folded into a line rather than an arrow.</summary>
    public double ThetaAt(int x, int y)
    {
        int k = (y * Width) + x;
        return 0.5 * Math.Atan2(_vy[k], _vx[k]);
    }

    /// <summary>How sure the tensor is of that orientation at a grid cell, 0..1.</summary>
    public double ConfidenceAt(int x, int y)
    {
        int k = (y * Width) + x;
        return Math.Sqrt((_vx[k] * _vx[k]) + (_vy[k] * _vy[k]));
    }

    /// <summary>
    /// Measures the tensor over a luminance grid, row-major, <paramref name="width"/> wide.
    /// </summary>
    /// <param name="integrationPixels">
    /// How far the evidence for one orientation is gathered, in grid pixels — the tensor's
    /// integration scale. An edge is a line, so its evidence reaches only about this far; ask for
    /// less than the reach you intend to use and the answer will be "sure of nothing" everywhere
    /// but a hairline along each edge. Zero keeps the narrow default the courses were tuned on.
    /// </param>
    public static StructureTensor Compute(
        ReadOnlySpan<double> luminance, int width, int height, double integrationPixels = 0.0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (luminance.Length != width * height)
        {
            throw new ArgumentException(
                $"Expected {width * height} samples for {width}x{height}, got {luminance.Length}.",
                nameof(luminance));
        }

        var jxx = new double[width * height];
        var jyy = new double[width * height];
        var jxy = new double[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double gx = Sobel(luminance, width, height, x, y, horizontal: true);
                double gy = Sobel(luminance, width, height, x, y, horizontal: false);
                int k = (y * width) + x;
                jxx[k] = gx * gx;
                jyy[k] = gy * gy;
                jxy[k] = gx * gy;
            }
        }

        // Smoothed so the orientation is stable over a stretch rather than per-pixel noise. How
        // long a stretch is the caller's business: courses want the narrow default, the flattener
        // wants evidence to carry as far as the kernel it steers.
        Integrate(jxx, width, height, integrationPixels);
        Integrate(jyy, width, height, integrationPixels);
        Integrate(jxy, width, height, integrationPixels);

        var vx = new double[width * height];
        var vy = new double[width * height];
        var coherence = new double[width * height];
        double maxCoherence = 1e-9;
        for (int k = 0; k < vx.Length; k++)
        {
            coherence[k] = Math.Sqrt(((jxx[k] - jyy[k]) * (jxx[k] - jyy[k])) + (4.0 * jxy[k] * jxy[k]));
            maxCoherence = Math.Max(maxCoherence, coherence[k]);

            // gradient double-angle vector: (Jxx - Jyy, 2 Jxy); negate for the perpendicular.
            vx[k] = -(jxx[k] - jyy[k]);
            vy[k] = -(2.0 * jxy[k]);
        }

        // Confidence is normalised against a high percentile of the frame, not its single strongest
        // cell: one specular highlight or one hard contour used to set the divisor and push every
        // other cell's vector to nearly zero, so the diffused field was a constant 0.08 plateau
        // echoing the frame rather than the subject. A percentile lets a photograph of soft
        // gradients still hand the caller a real direction to hold onto.
        double confidenceScale = Math.Max(1e-9, ContourSet.Percentile(coherence, 0.95));

        var edge = new double[vx.Length];
        for (int k = 0; k < vx.Length; k++)
        {
            double length = Math.Sqrt((vx[k] * vx[k]) + (vy[k] * vy[k]));

            // edge feeds ContourSet.LevelFor / FigureMask, which threshold it against absolute
            // constants — it stays on the frame maximum so the silhouette detection is untouched.
            edge[k] = Math.Min(1.0, length / maxCoherence);

            double weight = Math.Min(1.0, length / confidenceScale);
            if (length > 1e-12)
            {
                vx[k] = vx[k] / length * weight;
                vy[k] = vy[k] / length * weight;
            }
        }

        Blur(edge, width, height);

        return new StructureTensor(width, height, vx, vy, edge);
    }

    private static double Sobel(ReadOnlySpan<double> source, int width, int height, int x, int y, bool horizontal)
    {
        int xm = Math.Clamp(x - 1, 0, width - 1), x0 = Math.Clamp(x, 0, width - 1), xp = Math.Clamp(x + 1, 0, width - 1);
        int ym = Math.Clamp(y - 1, 0, height - 1), y0 = Math.Clamp(y, 0, height - 1), yp = Math.Clamp(y + 1, 0, height - 1);

        return horizontal
            ? (source[(ym * width) + xp] + (2 * source[(y0 * width) + xp]) + source[(yp * width) + xp])
              - (source[(ym * width) + xm] + (2 * source[(y0 * width) + xm]) + source[(yp * width) + xm])
            : (source[(yp * width) + xm] + (2 * source[(yp * width) + x0]) + source[(yp * width) + xp])
              - (source[(ym * width) + xm] + (2 * source[(ym * width) + x0]) + source[(ym * width) + xp]);
    }

    /// <summary>
    /// Gathers a tensor component over <paramref name="pixels"/>. At the default the narrow 1-2-1
    /// stack is used unchanged, so the field the courses follow is the field it always was.
    /// </summary>
    private static void Integrate(double[] field, int width, int height, double pixels)
    {
        if (pixels <= 1.5)
        {
            Blur(field, width, height);
            return;
        }

        // Three box passes approximate a Gaussian of σ ≈ radius.
        int radius = Math.Max(1, (int)Math.Round(pixels * Math.Sqrt(3.0 / 4.0)));
        for (int pass = 0; pass < 3; pass++)
        {
            BoxBlur(field, width, height, radius);
        }
    }

    private static void BoxBlur(double[] field, int width, int height, int radius)
    {
        var scratch = new double[field.Length];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double sum = 0.0;
                for (int d = -radius; d <= radius; d++)
                {
                    sum += field[(y * width) + Math.Clamp(x + d, 0, width - 1)];
                }

                scratch[(y * width) + x] = sum / ((2 * radius) + 1);
            }
        }

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                double sum = 0.0;
                for (int d = -radius; d <= radius; d++)
                {
                    sum += scratch[(Math.Clamp(y + d, 0, height - 1) * width) + x];
                }

                field[(y * width) + x] = sum / ((2 * radius) + 1);
            }
        }
    }

    /// <summary>Separable 1-2-1 blur, three passes — the narrow default.</summary>
    private static void Blur(double[] field, int width, int height)
    {
        var scratch = new double[field.Length];
        for (int pass = 0; pass < 3; pass++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int xm = Math.Max(0, x - 1);
                    int xp = Math.Min(width - 1, x + 1);
                    scratch[(y * width) + x] =
                        (field[(y * width) + xm] + (2.0 * field[(y * width) + x]) + field[(y * width) + xp]) / 4.0;
                }
            }

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    int ym = Math.Max(0, y - 1);
                    int yp = Math.Min(height - 1, y + 1);
                    field[(y * width) + x] =
                        (scratch[(ym * width) + x] + (2.0 * scratch[(y * width) + x]) + scratch[(yp * width) + x]) / 4.0;
                }
            }
        }
    }
}
