using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Optics;
using MosaicGenerator.Core.Material;
using MosaicGenerator.Core.Rendering;
using MosaicGenerator.Core.Skia;
using MosaicGenerator.Core.Tests.Support;
using SkiaSharp;

namespace MosaicGenerator.Core.Tests.Skia;

public class SkiaMosaicRendererTests
{
    private readonly SkiaMosaicRenderer _renderer = new();

    [Fact]
    public void TheCartoonIsAValidPngOfTheExpectedSize()
    {
        RenderPlan plan = Plan();

        byte[] png = _renderer.RenderCartoon(plan);

        using SKBitmap? decoded = SKBitmap.Decode(png);
        Assert.NotNull(decoded);
        Assert.Equal(plan.PixelWidth, decoded!.Width);
        Assert.Equal(plan.PixelHeight, decoded.Height);
    }

    [Fact]
    public void TheMarginShowsTheJointColourTheLayoutImplies()
    {
        // 300 x 200 mm at a 23 mm step leaves a 2 mm margin, so the corner pixel is bare joint.
        RenderPlan plan = Plan();

        using SKBitmap decoded = SKBitmap.Decode(_renderer.RenderCartoon(plan))!;

        SKColor corner = decoded.GetPixel(0, 0);
        Assert.Equal(plan.JointColor.ToBytes(), (corner.Red, corner.Green, corner.Blue));
    }

    [Fact]
    public void ANarrowJointReadsDarkerThanAWideOne()
    {
        // Same white adhesive either way. A 1 mm slot beside 8 mm tesserae is a deep, narrow
        // crack that almost nothing reaches; a 3 mm one is open enough to stay mid grey.
        RenderPlan fine = RenderGeometry.Compute(
            PlanFactory.Striped(seed: 5, module: 4, grout: 1), RenderOptions.Cartoon);
        RenderPlan coarse = RenderGeometry.Compute(
            PlanFactory.Striped(seed: 5, module: 20, grout: 3), RenderOptions.Cartoon);

        Assert.True(
            fine.JointColor.ToLab().L < coarse.JointColor.ToLab().L - 10,
            $"fine joint L* {fine.JointColor.ToLab().L:0.0} should sit well below "
            + $"coarse {coarse.JointColor.ToLab().L:0.0}");

        // Still a long way below the adhesive seen in the open, which is the whole point.
        Assert.True(coarse.JointColor.ToLab().L < JointAppearance.AdhesiveWhite.ToLab().L - 25);
    }

    [Fact]
    public void TheCartoonPaintsEachModuleFlatInItsPaletteColour()
    {
        RenderPlan plan = Plan();

        using SKBitmap decoded = SKBitmap.Decode(_renderer.RenderCartoon(plan))!;

        RenderedModule module = plan.Modules.Single(m => m is { Row: 2, Column: 3 });
        SKColor centre = decoded.GetPixel(
            (int)(module.Bounds.X + (module.Bounds.Width / 2)),
            (int)(module.Bounds.Y + (module.Bounds.Height / 2)));

        // Flat fill: the centre of the module is exactly its article colour.
        (byte r, byte g, byte b) = module.FillColor.ToBytes();
        Assert.Equal((r, g, b), (centre.Red, centre.Green, centre.Blue));
    }

    [Fact]
    public void TheSchemeIsWhiteWithDarkOutlinesAndCodes()
    {
        MosaicPlan plan = PlanFactory.Striped(seed: 5);
        RenderPlan rendered = RenderGeometry.Compute(plan, RenderOptions.Scheme);
        MaterialReport report = MaterialCalculator.Calculate(plan, 1.25, 1500m);

        using SKBitmap decoded = SKBitmap.Decode(_renderer.RenderScheme(rendered, report))!;

        int white = 0;
        int dark = 0;
        for (int y = 0; y < decoded.Height; y++)
        {
            for (int x = 0; x < decoded.Width; x++)
            {
                byte red = decoded.GetPixel(x, y).Red;
                if (red > 240)
                {
                    white++;
                }
                else if (red < 90)
                {
                    dark++;
                }
            }
        }

        long total = (long)decoded.Width * decoded.Height;
        Assert.True(white > total * 0.6, $"scheme should be mostly white, got {white}/{total}");
        Assert.True(dark > total * 0.01, $"outlines and codes should be present, got {dark}/{total}");
    }

    [Fact]
    public void BothOutputsCarryThePhysicalScale()
    {
        MosaicPlan plan = PlanFactory.Striped(seed: 5);
        RenderPlan cartoon = RenderGeometry.Compute(plan, RenderOptions.Cartoon);
        RenderPlan scheme = RenderGeometry.Compute(plan, RenderOptions.Scheme);
        MaterialReport report = MaterialCalculator.Calculate(plan, 1.25, 1500m);

        double? cartoonScale = PngMetadata.ReadPhysicalScale(_renderer.RenderCartoon(cartoon));
        double? schemeScale = PngMetadata.ReadPhysicalScale(_renderer.RenderScheme(scheme, report));

        Assert.Equal(cartoon.PixelsPerMm, cartoonScale!.Value, 1e-3);
        Assert.Equal(scheme.PixelsPerMm, schemeScale!.Value, 1e-3);

        // The cartoon is rendered at twice the scheme's resolution — it prints 1:1.
        Assert.True(cartoonScale.Value > schemeScale.Value);
    }

    [Fact]
    public void RenderingIsReproducible()
    {
        RenderPlan first = Plan();
        RenderPlan second = Plan();

        Assert.Equal(_renderer.RenderCartoon(first), _renderer.RenderCartoon(second));
    }

    private static RenderPlan Plan() =>
        RenderGeometry.Compute(PlanFactory.Striped(seed: 5), RenderOptions.Cartoon);
}
