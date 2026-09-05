using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Grid;

namespace MosaicGenerator.Core.Imaging;

/// <summary>
/// Flattens the photograph into plateaus *before* it is sampled, with the radius measured in
/// tesserae rather than pixels: everything finer than a piece of smalt is meant to vanish, because
/// averaging it under a tessera turns it into noise and the tone stretch then multiplies that noise
/// into crumb. Measured over eight photographs — see docs/ploskosti-spike.md.
///
/// The kernel is an ellipse, not a disc, and that is the whole of the second idea. A mosaicist lays
/// along the form, so detail *along* the form is detail no course can show and may be spent freely;
/// detail *across* it is the one thing a piece can carry — the step between two neighbouring
/// courses. So the long axis runs along the local orientation from <see cref="StructureTensor"/>
/// and the short axis across it. A round kernel of the reach the courses want ate the sitter's brow
/// along with the pores; this one does not. Where the tensor is sure of nothing — flat sky, blank
/// ground — the ellipse degenerates to a disc, which is right: with no direction in the picture
/// there is nothing to preserve across. See docs/anizotropnoe-uploshchenie-plan.md.
///
/// The range term (ΔE) rides on top of both axes, so a strong colour boundary still stops the blur
/// outright, whichever way it runs.
/// </summary>
public static class ImageFlattener
{
    /// <summary>Working resolution: how many pixels one tessera gets while the filter runs.</summary>
    private const double WorkPixelsPerTessera = 8.0;

    /// <summary>
    /// Returns a copy of <paramref name="image"/> at the same dimensions, flattened into plateaus
    /// at tessera scale.
    /// </summary>
    /// <param name="tesseraPixels">Width of one tessera in source pixels.</param>
    public static SourceImage Apply(SourceImage image, double tesseraPixels, FlattenSettings settings)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.RadiusTesserae <= 0 || settings.Iterations <= 0)
        {
            return image;
        }

        double scale = Math.Min(1.0, WorkPixelsPerTessera / Math.Max(1e-6, tesseraPixels));
        int w = Math.Max(4, (int)Math.Round(image.Width * scale));
        int h = Math.Max(4, (int)Math.Round(image.Height * scale));

        Plane work = Downscale(image, w, h);

        // Measured once, off the photograph as it arrived. Re-measuring it between iterations would
        // steer the filter by its own output, and the direction would drift toward whatever the
        // first pass happened to leave.
        double sigmaAlong = settings.RadiusTesserae * Math.Min(WorkPixelsPerTessera, tesseraPixels);
        double across = Math.Clamp(settings.AcrossFraction, 0.0, 1.0);

        // Measured once, off the photograph as it arrived. Re-measuring it between iterations would
        // steer the filter by its own output, and the direction would drift toward whatever the
        // first pass happened to leave.
        //
        // The evidence is gathered over the kernel's own reach, not over the two or three pixels
        // the courses use. Measured on a synthetic edge: with the narrow scale the tensor is sure
        // within three pixels of an edge and collapses to zero beyond, so the ellipse was an
        // ellipse in a hairline and a disc everywhere else — including the pixels either side of a
        // brow, which the disc then smears across it. The form does not stop at the edge pixel.
        StructureTensor tensor = StructureTensor.Compute(work.L, w, h, sigmaAlong);

        for (int i = 0; i < settings.Iterations; i++)
        {
            work = OrientedPass(work, w, h, tensor, sigmaAlong, across, settings.RangeDe, along: true);
            work = OrientedPass(work, w, h, tensor, sigmaAlong, across, settings.RangeDe, along: false);
        }

        return Upscale(work, w, h, image.Width, image.Height);
    }

    /// <summary>The working image as three Lab planes, row-major.</summary>
    private readonly record struct Plane(double[] L, double[] A, double[] B);

    private static Plane Downscale(SourceImage image, int w, int h)
    {
        var L = new double[w * h];
        var A = new double[w * h];
        var B = new double[w * h];

        for (int y = 0; y < h; y++)
        {
            int y0 = (int)((long)y * image.Height / h);
            int y1 = Math.Max(y0 + 1, (int)((long)(y + 1) * image.Height / h));
            for (int x = 0; x < w; x++)
            {
                int x0 = (int)((long)x * image.Width / w);
                int x1 = Math.Max(x0 + 1, (int)((long)(x + 1) * image.Width / w));

                // Averaged in linear light, then read as Lab: averaging sRGB darkens every edge.
                CieLab lab = image.AverageLinear(x0, y0, x1 - x0, y1 - y0).ToLab();
                int t = (y * w) + x;
                L[t] = lab.L;
                A[t] = lab.A;
                B[t] = lab.B;
            }
        }

        return new Plane(L, A, B);
    }

    /// <summary>
    /// One bilateral sweep along a per-pixel axis: the tensor's orientation when
    /// <paramref name="along"/>, its perpendicular otherwise. The perpendicular sweep is the one
    /// that gets shortened, and by how much is what the tensor's confidence decides.
    /// </summary>
    private static Plane OrientedPass(
        Plane src, int w, int h, StructureTensor tensor,
        double sigmaAlong, double acrossFraction, double sigmaRange, bool along)
    {
        double rangeDenom = 2.0 * sigmaRange * sigmaRange;
        var outL = new double[src.L.Length];
        var outA = new double[src.L.Length];
        var outB = new double[src.L.Length];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int c = (y * w) + x;
                double theta = tensor.ThetaAt(x, y);
                double confidence = Math.Clamp(tensor.ConfidenceAt(x, y), 0.0, 1.0);

                // Sure of a direction: the perpendicular axis shrinks to acrossFraction of the
                // reach. Sure of nothing: both axes keep the full reach and the ellipse is a disc.
                double sigma = along
                    ? sigmaAlong
                    : sigmaAlong * (acrossFraction + ((1.0 - acrossFraction) * (1.0 - confidence)));

                double stepX = along ? Math.Cos(theta) : -Math.Sin(theta);
                double stepY = along ? Math.Sin(theta) : Math.Cos(theta);

                int radius = Math.Max(1, (int)Math.Ceiling(2.0 * sigma));
                double cl = src.L[c], ca = src.A[c], cb = src.B[c];
                double sum = 0, sl = 0, sa = 0, sb = 0;

                for (int d = -radius; d <= radius; d++)
                {
                    double sx = x + (d * stepX);
                    double sy = y + (d * stepY);
                    (double nl, double na, double nb) = Sample(src, w, h, sx, sy);

                    double dl = nl - cl, da = na - ca, db = nb - cb;
                    double de2 = (dl * dl) + (da * da) + (db * db);
                    double weight = Math.Exp(-(d * d) / (2.0 * sigma * sigma)) * Math.Exp(-de2 / rangeDenom);

                    sum += weight;
                    sl += weight * nl;
                    sa += weight * na;
                    sb += weight * nb;
                }

                outL[c] = sl / sum;
                outA[c] = sa / sum;
                outB[c] = sb / sum;
            }
        }

        return new Plane(outL, outA, outB);
    }

    /// <summary>Bilinear read at a fractional position, clamped at the frame.</summary>
    private static (double L, double A, double B) Sample(Plane src, int w, int h, double sx, double sy)
    {
        double fx = Math.Clamp(sx, 0, w - 1);
        double fy = Math.Clamp(sy, 0, h - 1);
        int x0 = (int)fx, x1 = Math.Min(x0 + 1, w - 1);
        int y0 = (int)fy, y1 = Math.Min(y0 + 1, h - 1);
        double tx = fx - x0, ty = fy - y0;

        return (Lerp2(src.L, w, x0, x1, y0, y1, tx, ty),
                Lerp2(src.A, w, x0, x1, y0, y1, tx, ty),
                Lerp2(src.B, w, x0, x1, y0, y1, tx, ty));
    }

    private static SourceImage Upscale(Plane src, int w, int h, int width, int height)
    {
        var rgb = new byte[(long)width * height * 3];
        int t = 0;

        for (int y = 0; y < height; y++)
        {
            double fy = Math.Clamp(((y + 0.5) * h / height) - 0.5, 0, h - 1);
            int y0 = (int)fy, y1 = Math.Min(y0 + 1, h - 1);
            double wy = fy - y0;

            for (int x = 0; x < width; x++)
            {
                double fx = Math.Clamp(((x + 0.5) * w / width) - 0.5, 0, w - 1);
                int x0 = (int)fx, x1 = Math.Min(x0 + 1, w - 1);
                double wx = fx - x0;

                Rgb srgb = new CieLab(
                    Lerp2(src.L, w, x0, x1, y0, y1, wx, wy),
                    Lerp2(src.A, w, x0, x1, y0, y1, wx, wy),
                    Lerp2(src.B, w, x0, x1, y0, y1, wx, wy)).ToRgb();

                rgb[t++] = ToByte(srgb.R);
                rgb[t++] = ToByte(srgb.G);
                rgb[t++] = ToByte(srgb.B);
            }
        }

        return new SourceImage(rgb, width, height);
    }

    private static double Lerp2(double[] v, int w, int x0, int x1, int y0, int y1, double wx, double wy)
    {
        double top = (v[(y0 * w) + x0] * (1 - wx)) + (v[(y0 * w) + x1] * wx);
        double bottom = (v[(y1 * w) + x0] * (1 - wx)) + (v[(y1 * w) + x1] * wx);
        return (top * (1 - wy)) + (bottom * wy);
    }

    private static byte ToByte(double channel) =>
        (byte)Math.Clamp(Math.Round(channel * 255.0), 0.0, 255.0);
}
