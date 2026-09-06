using MosaicGenerator.Core.Grid;
using MosaicGenerator.Core.Rendering;

namespace MosaicGenerator.Core.Tests.Grid;

public class FieldGeometryTests
{
    [Fact]
    public void SquareOffLeavesAQuadrilateralUntouched()
    {
        PointD[] quad = [new(0, 0), new(10, 0), new(10, 7), new(0, 7)];

        PointD[] result = FieldGeometry.SquareOff(quad, 5.0);

        Assert.Same(quad, result);
    }

    [Fact]
    public void SquareOffFoldsAwayANickedCorner()
    {
        // A rectangle with a 1 mm shaving across one corner — the shape the Voronoi cut leaves where
        // a neighbouring course meets this one at an angle.
        PointD[] nicked =
        [
            new(0, 0),
            new(9, 0),
            new(10, 1),
            new(10, 7),
            new(0, 7),
        ];

        PointD[] result = FieldGeometry.SquareOff(nicked, 5.0);

        Assert.Equal(4, result.Length);
        // The fold lands the merged vertex partway towards where the two long edges would have
        // crossed (10, 0), so the area barely moves — much less than folding the nick to its bare
        // midpoint would take off.
        double area = FieldGeometry.Area(result);
        Assert.InRange(area, FieldGeometry.Area(nicked) - 2.5, 70.0);
    }

    [Fact]
    public void SquareOffKeepsAGenuineFiveSidedPieceWhenEveryEdgeIsLongEnough()
    {
        // No edge shorter than the knap threshold: this is a piece a hand can cut, so it stays.
        PointD[] pentagon =
        [
            new(0, 0),
            new(10, 0),
            new(14, 8),
            new(7, 14),
            new(0, 8),
        ];

        PointD[] result = FieldGeometry.SquareOff(pentagon, 5.0);

        Assert.Equal(5, result.Length);
    }

    [Fact]
    public void SquareOffDropsAVertexThatBarelyBendsTheOutline()
    {
        // The middle vertex sits 0.05 mm off the straight run between its neighbours — clip noise.
        PointD[] withKink =
        [
            new(0, 0),
            new(5, 0.05),
            new(10, 0),
            new(10, 7),
            new(0, 7),
        ];

        PointD[] result = FieldGeometry.SquareOff(withKink, 5.0);

        Assert.Equal(4, result.Length);
    }
}
