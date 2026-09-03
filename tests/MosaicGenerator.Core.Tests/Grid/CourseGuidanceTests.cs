using MosaicGenerator.Core.Grid;
using MosaicGenerator.Core.Imaging;
using MosaicGenerator.Core.Rendering;
using MosaicGenerator.Core.Tests.Support;

namespace MosaicGenerator.Core.Tests.Grid;

public class CourseGuidanceTests
{
    private const int Cells = 65;
    private const double SideMm = 128.0;

    [Fact]
    public void WhereTheTextureIsConfidentTheGuidanceFollowsIt()
    {
        // Horizontal stripes: the tensor is sure everywhere, so the guidance is the tensor and the
        // contour echo never gets a say.
        DirectionField field = FieldOf(Stripes());
        CourseGuidance guidance = Build(field);

        foreach ((double u, double v) in new[] { (0.3, 0.3), (0.5, 0.6), (0.7, 0.45) })
        {
            Assert.Equal(field.ThetaAt(u, v), guidance.ThetaAt(u, v), 0.15);
        }
    }

    [Fact]
    public void WhereTheTextureSaysNothingTheGuidanceEchoesTheContour()
    {
        // A flat grey field with one dark disc. Away from the disc the tensor has no direction at
        // all, so the guidance has to fall back on the echo of the silhouette — that is opus
        // musivum, and it is what keeps the background from reading as a plain grid.
        DirectionField field = FieldOf(Disc());
        CourseGuidance guidance = Build(field);

        // Straight above the disc's centre the iso-distance line runs horizontally.
        Assert.Equal(0.0, NormaliseHalfTurn(guidance.ThetaAt(0.5, 0.12)), 0.3);

        // Beside it, vertically.
        Assert.Equal(Math.PI / 2.0, Math.Abs(NormaliseHalfTurn(guidance.ThetaAt(0.1, 0.5))), 0.35);
    }

    [Fact]
    public void WithNoContourAtAllTheGuidanceIsStillDefinedEverywhere()
    {
        DirectionField field = FieldOf(Flat());
        CourseGuidance guidance = CourseGuidance.Build(field, SideMm, SideMm, [], 8.0, 8.0);

        for (double u = 0.0; u <= 1.0; u += 0.1)
        {
            for (double v = 0.0; v <= 1.0; v += 0.1)
            {
                Assert.False(double.IsNaN(guidance.ThetaAt(u, v)));
            }
        }
    }

    [Fact]
    public void OnlyTheLongContoursSteerTheBackground()
    {
        // A scrap of contour is a real edge and still deserves its own courses, but if the
        // background echoed it the layout would repeat the detail instead of the silhouette. With
        // nothing but scraps on offer the longest one is taken rather than none, so the background
        // always has something to echo.
        DirectionField field = FieldOf(Disc());
        PointD[] scrap = [new(60.0, 60.0), new(64.0, 62.0)];

        CourseGuidance withScrap = CourseGuidance.Build(
            field, SideMm, SideMm, [LongContour(), scrap], 8.0, 8.0);
        CourseGuidance without = CourseGuidance.Build(
            field, SideMm, SideMm, [LongContour()], 8.0, 8.0);

        for (double u = 0.1; u < 1.0; u += 0.2)
        {
            Assert.Equal(without.ThetaAt(u, 0.2), withScrap.ThetaAt(u, 0.2), 1e-9);
        }
    }

    private static CourseGuidance Build(DirectionField field) =>
        CourseGuidance.Build(
            field, SideMm, SideMm,
            ContourSet.Extract(field, SideMm, SideMm, 8.0), 8.0, 8.0);

    private static PointD[] LongContour() =>
        [.. Enumerable.Range(0, 33).Select(i => new PointD(SideMm / 2.0, i * SideMm / 32.0))];

    private static DirectionField FieldOf(SourceImage image)
    {
        var crop = new CropRect(0, 0, image.Width, image.Height);
        return DirectionField.Compute(image, crop, 1.0, Cells);
    }

    private static SourceImage Stripes() =>
        ImageFactory.FromPixels(128, 128, (_, y) => (y / 4 % 2 == 0) ? "#282828" : "#C8C8C8");

    private static SourceImage Disc() =>
        ImageFactory.FromPixels(128, 128, (x, y) =>
        {
            double dx = x - 64.0;
            double dy = y - 64.0;
            return Math.Sqrt((dx * dx) + (dy * dy)) < 24.0 ? "#1E1E1E" : "#D2D2D2";
        });

    private static SourceImage Flat() => ImageFactory.Solid(128, 128, "#808080");

    private static double NormaliseHalfTurn(double theta)
    {
        double t = theta % Math.PI;
        if (t > Math.PI / 2.0)
        {
            t -= Math.PI;
        }
        else if (t < -Math.PI / 2.0)
        {
            t += Math.PI;
        }

        return t;
    }
}
