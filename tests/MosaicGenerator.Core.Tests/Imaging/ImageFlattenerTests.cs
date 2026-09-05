using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Imaging;

namespace MosaicGenerator.Core.Tests.Imaging;

/// <summary>
/// The flattener's whole claim is directional: it may lose detail *along* the form, where a course
/// of tesserae shows nothing anyway, and must keep it *across* the form, which is the only
/// direction a piece of smalt can carry. These tests are that claim, spelled out.
/// </summary>
public class ImageFlattenerTests
{
    private const int TesseraPixels = 8;

    [Fact]
    public void AFlatImageComesBackUnchanged()
    {
        SourceImage image = Grey(64, 64, (_, _) => 128);

        SourceImage flat = ImageFlattener.Apply(image, TesseraPixels, Settings());

        for (int y = 8; y < 56; y += 8)
        {
            for (int x = 8; x < 56; x += 8)
            {
                Assert.Equal(128, image.GetPixel(x, y).ToBytes().R);
                Assert.InRange(flat.GetPixel(x, y).ToBytes().R, 126, 130);
            }
        }
    }

    [Fact]
    public void GrainAlongTheFormIsWipedOut()
    {
        SourceImage image = DiagonalEdgeWithGrain();

        SourceImage flat = ImageFlattener.Apply(image, TesseraPixels, Settings());

        double before = GrainAmplitude(image);
        double after = GrainAmplitude(flat);
        Assert.True(
            after < before * 0.5,
            $"grain along the form survived: {before:0.00} → {after:0.00} ΔL");
    }

    [Fact]
    public void TheEdgeAcrossTheFormOutlivesAnIsotropicKernelOfTheSameReach()
    {
        // The range term is opened wide on purpose (ΔE 25 against a step of some 11 ΔE) so it
        // protects nothing: what is left is the spatial kernel alone, which is what changed.
        SourceImage image = DiagonalEdgeWithGrain();

        double original = EdgeContrast(image);
        double anisotropic = EdgeContrast(ImageFlattener.Apply(image, TesseraPixels, Settings(across: 0.25)));
        double isotropic = EdgeContrast(ImageFlattener.Apply(image, TesseraPixels, Settings(across: 1.0)));

        Assert.True(
            anisotropic > isotropic * 1.3,
            $"anisotropy bought nothing: круглое ядро {isotropic:0.00}, эллипс {anisotropic:0.00} ΔL " +
            $"(исходно {original:0.00})");
        Assert.True(
            anisotropic > original * 0.7,
            $"the edge was eaten anyway: {original:0.00} → {anisotropic:0.00} ΔL");
    }

    [Fact]
    public void AnUnsteeredKernelStaysRound()
    {
        // Where the tensor is sure of nothing there is no direction to preserve, so the ellipse
        // must degenerate to a circle rather than pick an arbitrary axis. A flat field is that case.
        SourceImage image = Grey(64, 64, (_, _) => 128);

        SourceImage narrow = ImageFlattener.Apply(image, TesseraPixels, Settings(across: 0.1));
        SourceImage round = ImageFlattener.Apply(image, TesseraPixels, Settings(across: 1.0));

        for (int y = 8; y < 56; y += 8)
        {
            for (int x = 8; x < 56; x += 8)
            {
                Assert.Equal(L(round, x, y), L(narrow, x, y), 0.5);
            }
        }
    }

    [Fact]
    public void NoRadiusMeansNoWork()
    {
        SourceImage image = DiagonalEdgeWithGrain();

        Assert.Same(image, ImageFlattener.Apply(image, TesseraPixels, Settings(radius: 0.0)));
        Assert.Same(image, ImageFlattener.Apply(image, TesseraPixels, Settings(iterations: 0)));
    }

    [Fact]
    public void TheFlatteningIsDeterministic()
    {
        SourceImage image = DiagonalEdgeWithGrain();

        SourceImage a = ImageFlattener.Apply(image, TesseraPixels, Settings());
        SourceImage b = ImageFlattener.Apply(image, TesseraPixels, Settings());

        for (int y = 0; y < 96; y += 3)
        {
            for (int x = 0; x < 96; x += 3)
            {
                Assert.Equal(a.GetPixel(x, y).ToBytes().R, b.GetPixel(x, y).ToBytes().R);
            }
        }
    }


    private static FlattenSettings Settings(
        double radius = 1.0, double rangeDe = 25.0, int iterations = 2, double across = 0.25) =>
        new(radius, rangeDe, iterations, across);

    /// <summary>
    /// A soft step across the diagonal y = x, speckled with fine grain. The step is the feature the
    /// material can carry — a course either side of it shows the difference; the grain is texture no
    /// piece can hold. The grain is deliberately *directionless*: a striped texture would have an
    /// orientation of its own, and the tensor would rightly steer by the stripes instead of the edge.
    /// </summary>
    private static SourceImage DiagonalEdgeWithGrain() =>
        Grey(96, 96, (x, y) =>
        {
            int step = y > x ? 138 : 110;
            int grain = (((x * 73856093) ^ (y * 19349663)) & 0xFF) < 128 ? 8 : -8;
            return step + grain;
        });

    /// <summary>Swing of the grain, in ΔL, read along a line parallel to the edge and just off it.</summary>
    private static double GrainAmplitude(SourceImage image)
    {
        var samples = new List<double>();
        for (int t = -20; t <= 20; t++)
        {
            // Along the edge, four pixels into the lighter half.
            samples.Add(L(image, 44 + t, 52 + t));
        }

        double mean = samples.Average();
        return Math.Sqrt(samples.Sum(v => (v - mean) * (v - mean)) / samples.Count);
    }

    /// <summary>
    /// Lightness difference across the edge, read close enough to it that the tensor is still sure
    /// which way the edge runs. Far out there is only grain, the ellipse rightly turns back into a
    /// disc, and the measurement would say nothing about anisotropy.
    /// </summary>
    private static double EdgeContrast(SourceImage image)
    {
        double lighter = 0.0, darker = 0.0;
        int n = 0;
        for (int t = -16; t <= 16; t += 4)
        {
            // Perpendicular to y = x is (1, -1); step four pixels off the edge each way.
            lighter += L(image, 44 + t, 52 + t);
            darker += L(image, 52 + t, 44 + t);
            n++;
        }

        return (lighter / n) - (darker / n);
    }

    private static double L(SourceImage image, int x, int y) => image.GetPixel(x, y).ToLab().L;

    private static SourceImage Grey(int width, int height, Func<int, int, int> greyAt)
    {
        var rgb = new byte[width * height * 3];
        int offset = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte g = (byte)Math.Clamp(greyAt(x, y), 0, 255);
                rgb[offset++] = g;
                rgb[offset++] = g;
                rgb[offset++] = g;
            }
        }

        return new SourceImage(rgb, width, height);
    }
}
