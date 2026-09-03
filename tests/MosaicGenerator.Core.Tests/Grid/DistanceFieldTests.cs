using MosaicGenerator.Core.Grid;
using MosaicGenerator.Core.Rendering;

namespace MosaicGenerator.Core.Tests.Grid;

public class DistanceFieldTests
{
    private const int Cells = 129;
    private const double SideMm = 128.0;

    [Fact]
    public void ThePanelEdgeDoesNotSteerTheField()
    {
        // A single vertical contour down the middle: the whole field echoes it, right up to the top
        // edge of the panel, because the edge is not a barrier. Seeding the frame at distance zero
        // used to turn this strip horizontal — that is what made the background read as nested
        // rectangles rather than as the silhouette.
        DistanceField field = DistanceField.Build(
            Cells, Cells, SideMm, SideMm, [Vertical(SideMm / 2.0)]);

        foreach (double u in new[] { 0.2, 0.3, 0.7 })
        {
            Assert.Equal(Math.PI / 2.0, Math.Abs(NormaliseHalfTurn(field.TangentAt(u, 0.02))), 0.15);
        }
    }

    [Fact]
    public void TheTangentRunsAroundAContourRatherThanAtIt()
    {
        // A short contour near the centre: the background course at any offset should run across the
        // radius from it, not along it. That is the echo of the silhouette — opus musivum.
        DistanceField field = DistanceField.Build(
            Cells, Cells, SideMm, SideMm, [Vertical(SideMm / 2.0, from: 0.45, to: 0.55)]);

        // Sampled straight above the contour's centre, the iso-distance line is horizontal.
        Assert.Equal(0.0, NormaliseHalfTurn(field.TangentAt(0.5, 0.2)), 0.2);

        // Sampled beside it, it is vertical.
        Assert.Equal(Math.PI / 2.0, Math.Abs(NormaliseHalfTurn(field.TangentAt(0.15, 0.5))), 0.25);
    }

    [Fact]
    public void SmoothingLeavesNoNaNAnywhere()
    {
        foreach (IReadOnlyList<PointD[]> contours in new IReadOnlyList<PointD[]>[]
                 {
                     [],
                     [Vertical(SideMm / 2.0)],
                 })
        {
            DistanceField field = DistanceField.Build(Cells, Cells, SideMm, SideMm, contours, 6.0);

            for (double u = 0.0; u <= 1.0; u += 0.05)
            {
                for (double v = 0.0; v <= 1.0; v += 0.05)
                {
                    Assert.True(double.IsFinite(field.TangentAt(u, v)));
                    Assert.True(double.IsFinite(field.NormalisedDistanceAt(u, v)));
                }
            }
        }
    }

    [Fact]
    public void TheFieldIsDeterministic()
    {
        PointD[] contour = Vertical(SideMm / 2.0);

        DistanceField a = DistanceField.Build(Cells, Cells, SideMm, SideMm, [contour], 6.0);
        DistanceField b = DistanceField.Build(Cells, Cells, SideMm, SideMm, [contour], 6.0);

        for (double u = 0.0; u <= 1.0; u += 0.05)
        {
            for (double v = 0.0; v <= 1.0; v += 0.05)
            {
                Assert.Equal(a.TangentAt(u, v), b.TangentAt(u, v));
                Assert.Equal(a.NormalisedDistanceAt(u, v), b.NormalisedDistanceAt(u, v));
            }
        }
    }

    private static PointD[] Vertical(double x, double from = 0.0, double to = 1.0) =>
        [new PointD(x, from * SideMm), new PointD(x, to * SideMm)];

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
