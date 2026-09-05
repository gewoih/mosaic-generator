using MosaicGenerator.Core.Grid;

namespace MosaicGenerator.Core.Tests.Grid;

public class StructureTensorTests
{
    [Fact]
    public void AFlatFieldHasNoDirectionToOffer()
    {
        double[] flat = [.. Enumerable.Repeat(50.0, 64 * 64)];

        StructureTensor tensor = StructureTensor.Compute(flat, 64, 64);

        Assert.True(tensor.ConfidenceAt(32, 32) < 1e-6);
    }

    [Fact]
    public void AHorizontalEdgeOrientsAlongItself()
    {
        // Top half dark, bottom half light. The gradient runs vertically, so the orientation —
        // a quarter turn from it — runs horizontally.
        double[] luminance = Grid(64, 64, (_, y) => y < 32 ? 10.0 : 90.0);

        StructureTensor tensor = StructureTensor.Compute(luminance, 64, 64);

        Assert.Equal(0.0, Line(tensor.ThetaAt(32, 32)), 0.15);
        Assert.True(tensor.ConfidenceAt(32, 32) > 0.5);
    }

    [Fact]
    public void AVerticalEdgeOrientsAlongItself()
    {
        double[] luminance = Grid(64, 64, (x, _) => x < 32 ? 10.0 : 90.0);

        StructureTensor tensor = StructureTensor.Compute(luminance, 64, 64);

        Assert.True(
            Math.Abs(Line(tensor.ThetaAt(32, 32))) > (Math.PI / 2.0) - 0.2,
            $"expected near ±90°, got {Line(tensor.ThetaAt(32, 32)) * 180.0 / Math.PI:0}°");
    }

    [Fact]
    public void ADiagonalEdgeOrientsAlongItself()
    {
        // Light below the line y = x, dark above it: the edge runs at +45°, and so must the
        // orientation. This is the case the anisotropic filter leans on hardest — an axis-aligned
        // kernel cannot tell it apart from noise.
        double[] luminance = Grid(64, 64, (x, y) => y > x ? 90.0 : 10.0);

        StructureTensor tensor = StructureTensor.Compute(luminance, 64, 64);

        Assert.Equal(Math.PI / 4.0, Math.Abs(Line(tensor.ThetaAt(32, 32))), 0.2);
    }

    [Fact]
    public void TheMeasurementIsDeterministic()
    {
        double[] luminance = Grid(48, 32, (x, y) => ((x * 7) + (y * 13)) % 100);

        StructureTensor a = StructureTensor.Compute(luminance, 48, 32);
        StructureTensor b = StructureTensor.Compute(luminance, 48, 32);

        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 48; x++)
            {
                Assert.Equal(a.ThetaAt(x, y), b.ThetaAt(x, y), 1e-12);
            }
        }
    }

    [Fact]
    public void TheSampleCountMustMatchTheGrid()
    {
        Assert.Throws<ArgumentException>(() => StructureTensor.Compute(new double[10], 4, 4));
    }

    private static double[] Grid(int width, int height, Func<int, int, double> at)
    {
        var values = new double[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                values[(y * width) + x] = at(x, y);
            }
        }

        return values;
    }

    /// <summary>Folds an orientation into (−π/2, π/2]; orientation is a line, not an arrow.</summary>
    private static double Line(double theta)
    {
        double t = theta % Math.PI;
        if (t > Math.PI / 2.0) { t -= Math.PI; }
        else if (t <= -Math.PI / 2.0) { t += Math.PI; }
        return t;
    }
}
