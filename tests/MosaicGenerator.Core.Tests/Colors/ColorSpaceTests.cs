using MosaicGenerator.Core.Colors;

namespace MosaicGenerator.Core.Tests.Colors;

public class ColorSpaceTests
{
    private const double LabTolerance = 1e-3;

    // Reference values for the sRGB primaries under D65, 2-degree observer.
    [Theory]
    [InlineData("#FFFFFF", 100.0000, 0.0000, 0.0000)]
    [InlineData("#000000", 0.0000, 0.0000, 0.0000)]
    [InlineData("#808080", 53.5850, 0.0000, 0.0000)]
    [InlineData("#FF0000", 53.2408, 80.0925, 67.2032)]
    [InlineData("#00FF00", 87.7347, -86.1827, 83.1793)]
    [InlineData("#0000FF", 32.2970, 79.1875, -107.8602)]
    [InlineData("#FFFF00", 97.1393, -21.5537, 94.4780)]
    [InlineData("#00FFFF", 91.1132, -48.0875, -14.1312)]
    [InlineData("#FF00FF", 60.3242, 98.2343, -60.8249)]
    public void SrgbConvertsToKnownLabValues(string hex, double l, double a, double b)
    {
        CieLab lab = Rgb.FromHex(hex).ToLab();

        Assert.Equal(l, lab.L, LabTolerance);
        Assert.Equal(a, lab.A, LabTolerance);
        Assert.Equal(b, lab.B, LabTolerance);
    }

    [Theory]
    [InlineData("#FFFFFF")]
    [InlineData("#000000")]
    [InlineData("#808080")]
    [InlineData("#FF0000")]
    [InlineData("#1E4D2B")]
    [InlineData("#C8A2C8")]
    [InlineData("#0A0B0C")]
    public void LabRoundTripsBackToTheOriginalColor(string hex)
    {
        Rgb original = Rgb.FromHex(hex);

        Rgb restored = original.ToLab().ToRgb();

        // Exact at the precision the application actually stores and renders in.
        Assert.Equal(original.ToBytes(), restored.ToBytes());

        // Residual drift comes from the published XYZ -> linear RGB matrix, whose 7-digit
        // constants are not the exact inverse of the forward matrix; near black the linear
        // segment's 12.92 slope amplifies it.
        Assert.Equal(original.R, restored.R, 1e-5);
        Assert.Equal(original.G, restored.G, 1e-5);
        Assert.Equal(original.B, restored.B, 1e-5);
    }

    [Fact]
    public void SrgbToLinearUsesThePiecewiseTransferFunction()
    {
        // Above the breakpoint: the power segment, not a plain 2.2 gamma.
        Assert.Equal(0.21404114048223255, ColorSpace.SrgbToLinear(0.5), 1e-12);
        Assert.NotEqual(Math.Pow(0.5, 2.2), ColorSpace.SrgbToLinear(0.5), 1e-4);

        // Below the breakpoint: the linear segment.
        Assert.Equal(0.02 / 12.92, ColorSpace.SrgbToLinear(0.02), 1e-15);

        // The standard's own segments only meet at the breakpoint to within 1e-7.
        const double breakpoint = 0.04045;
        Assert.Equal(
            breakpoint / 12.92,
            Math.Pow((breakpoint + 0.055) / 1.055, 2.4),
            1e-7);
    }

    [Fact]
    public void LinearToSrgbInvertsSrgbToLinear()
    {
        foreach (double value in new[] { 0.0, 0.01, 0.04045, 0.25, 0.5, 0.75, 1.0 })
        {
            Assert.Equal(value, ColorSpace.LinearToSrgb(ColorSpace.SrgbToLinear(value)), 1e-12);
        }
    }

    [Theory]
    [InlineData("#FF8000")]
    [InlineData("ff8000")]
    [InlineData("  #Ff8000  ")]
    public void HexParsingAcceptsTheCommonSpellings(string hex)
    {
        Assert.Equal("#FF8000", Rgb.FromHex(hex).ToHex());
    }

    [Theory]
    [InlineData("#FFF")]
    [InlineData("#GGGGGG")]
    [InlineData("#FF80000")]
    [InlineData("")]
    public void HexParsingRejectsMalformedInput(string hex)
    {
        Assert.Throws<FormatException>(() => Rgb.FromHex(hex));
    }
}
