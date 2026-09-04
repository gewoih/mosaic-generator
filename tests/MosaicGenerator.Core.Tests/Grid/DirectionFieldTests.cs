using MosaicGenerator.Core.Grid;
using MosaicGenerator.Core.Imaging;
using MosaicGenerator.Core.Tests.Support;

namespace MosaicGenerator.Core.Tests.Grid;

public class DirectionFieldTests
{
    [Fact]
    public void AFeaturelessImageRelaxesToHorizontal()
    {
        SourceImage image = ImageFactory.Solid(200, 200, "#808080");

        DirectionField field = DirectionField.Compute(image, new CropRect(0, 0, 200, 200), fieldAspect: 1.0);

        for (double u = 0.2; u <= 0.8; u += 0.2)
        {
            for (double v = 0.2; v <= 0.8; v += 0.2)
            {
                Assert.Equal(0.0, NormaliseHalfTurn(field.ThetaAt(u, v)), 0.15);
            }
        }
    }

    [Fact]
    public void CoursesRunAlongAHorizontalEdge()
    {
        // Top half dark, bottom half light: the edge is horizontal, so the course orientation
        // along it is horizontal too — and near the edge the field should be sure of it.
        SourceImage image = ImageFactory.FromPixels(200, 200, (_, y) => y < 100 ? "#101010" : "#F0F0F0");

        DirectionField field = DirectionField.Compute(image, new CropRect(0, 0, 200, 200), fieldAspect: 1.0);

        Assert.Equal(0.0, NormaliseHalfTurn(field.ThetaAt(0.5, 0.5)), 0.2);
        Assert.True(field.CoherenceAt(0.5, 0.5) > 0.3);
    }

    [Fact]
    public void CoursesRunAlongAVerticalEdge()
    {
        // Left half dark, right half light: a vertical edge, so the course runs vertically —
        // orientation near ±90°.
        SourceImage image = ImageFactory.FromPixels(200, 200, (x, _) => x < 100 ? "#101010" : "#F0F0F0");

        DirectionField field = DirectionField.Compute(image, new CropRect(0, 0, 200, 200), fieldAspect: 1.0);

        double theta = Math.Abs(NormaliseHalfTurn(field.ThetaAt(0.5, 0.5)));
        Assert.True(theta > Math.PI / 2.0 - 0.25, $"expected near ±90°, got {theta * 180.0 / Math.PI:0}°");
    }

    [Fact]
    public void ALocalHighlightDoesNotSilenceASoftContour()
    {
        // A hard bright dot in one corner (a specular highlight) alongside a soft horizontal
        // contour across the middle. The highlight's gradient is far stronger, so normalising the
        // field's confidence against the frame maximum used to push the contour's coherence to
        // near zero. It must stay a direction the layout can follow.
        SourceImage image = ImageFactory.FromPixels(200, 200, (x, y) =>
        {
            if (x >= 10 && x < 24 && y >= 10 && y < 24)
            {
                return "#FFFFFF";
            }

            return y < 100 ? "#6E6E6E" : "#8A8A8A";
        });

        DirectionField field = DirectionField.Compute(image, new CropRect(0, 0, 200, 200), fieldAspect: 1.0);

        Assert.Equal(0.0, NormaliseHalfTurn(field.ThetaAt(0.5, 0.5)), 0.2);
        Assert.True(
            field.CoherenceAt(0.5, 0.5) > 0.2,
            $"soft contour lost its voice to the highlight: {field.CoherenceAt(0.5, 0.5):0.000}");
    }

    [Fact]
    public void TheFieldIsDeterministic()
    {
        SourceImage image = ImageFactory.Checkerboard(160, 120, "#202020", "#E0E0E0");
        var crop = new CropRect(0, 0, 160, 120);

        DirectionField a = DirectionField.Compute(image, crop, fieldAspect: 160.0 / 120.0);
        DirectionField b = DirectionField.Compute(image, crop, fieldAspect: 160.0 / 120.0);

        for (double u = 0.1; u < 1.0; u += 0.17)
        {
            for (double v = 0.1; v < 1.0; v += 0.19)
            {
                Assert.Equal(a.ThetaAt(u, v), b.ThetaAt(u, v), 1e-12);
            }
        }
    }

    /// <summary>Folds an orientation into (−π/2, π/2]; orientation is a line, not an arrow.</summary>
    private static double NormaliseHalfTurn(double theta)
    {
        double t = theta % Math.PI;
        if (t > Math.PI / 2.0)
        {
            t -= Math.PI;
        }
        else if (t <= -Math.PI / 2.0)
        {
            t += Math.PI;
        }

        return t;
    }
}
