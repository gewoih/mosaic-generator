using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Imaging;
using MosaicGenerator.Core.Tests.Support;

namespace MosaicGenerator.Core.Tests.Imaging;

public class LinearAveragingTests
{
    [Fact]
    public void HalfBlackHalfWhiteAveragesInLinearLightNotInSrgb()
    {
        SourceImage image = ImageFactory.Checkerboard(16, 16, "#000000", "#FFFFFF");

        LinearRgb average = image.AverageLinear(0, 0, 16, 16);

        Assert.Equal(0.5, average.R, 1e-12);
        Assert.Equal(0.5, average.G, 1e-12);
        Assert.Equal(0.5, average.B, 1e-12);

        // Linear 0.5 encodes back to sRGB 188. Averaging the sRGB bytes instead would give 128 —
        // a full stop darker, which is exactly the drift this pipeline has to avoid.
        (byte r, byte g, byte b) = average.ToSrgb().ToBytes();
        Assert.Equal(188, r);
        Assert.Equal(188, g);
        Assert.Equal(188, b);
        Assert.NotEqual(128, r);
    }

    [Fact]
    public void AverageOfAUniformAreaIsThatColour()
    {
        SourceImage image = ImageFactory.Solid(8, 8, "#3C6E71");

        LinearRgb average = image.AverageLinear(0, 0, 8, 8);

        Assert.Equal(Rgb.FromHex("#3C6E71").ToBytes(), average.ToSrgb().ToBytes());
    }

    [Fact]
    public void AverageClampsRectanglesThatRunPastTheEdge()
    {
        SourceImage image = ImageFactory.Solid(4, 4, "#FFFFFF");

        LinearRgb average = image.AverageLinear(-10, -10, 100, 100);

        Assert.Equal(1.0, average.R, 1e-12);
    }
}
