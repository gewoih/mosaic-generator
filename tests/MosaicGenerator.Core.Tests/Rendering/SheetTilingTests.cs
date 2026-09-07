using MosaicGenerator.Core.Rendering;

namespace MosaicGenerator.Core.Tests.Rendering;

public class SheetTilingTests
{
    // Cartoon resolution: 96 px per ~8 mm grid step ≈ 12 px/mm. Close enough for these assertions.
    private const double Ppm = 12.0;

    [Fact]
    public void APanelInsideOneA4IsASingleSheet()
    {
        SheetTiling tiling = SheetTiling.Plan(Px(180), Px(240), Ppm);

        Assert.Equal(1, tiling.Columns);
        Assert.Equal(1, tiling.Rows);
        SheetTile only = Assert.Single(tiling.Tiles);
        Assert.Null(only.SeamRightMm);
        Assert.Null(only.SeamBottomMm);
        Assert.Equal(0, only.SourceRectPx.X);
        Assert.Equal(0, only.SourceRectPx.Y);
    }

    [Fact]
    public void FourHundredMillimetresSquareNeedsAGrid()
    {
        SheetTiling tiling = SheetTiling.Plan(Px(400), Px(400), Ppm);

        Assert.Equal(3, tiling.Columns);
        Assert.Equal(2, tiling.Rows);
        Assert.Equal(6, tiling.Tiles.Count);
        Assert.False(tiling.Landscape);
    }

    [Fact]
    public void TheGridChoosesTheOrientationWithFewerSheets()
    {
        SheetTiling tiling = SheetTiling.Plan(Px(500), Px(140), Ppm);

        Assert.True(tiling.Landscape);
        Assert.Equal(297.0, tiling.PaperWidthMm);
        Assert.Equal(210.0, tiling.PaperHeightMm);
    }

    [Fact]
    public void NeighbouringSlicesOverlapAndTogetherCoverTheRaster()
    {
        int w = Px(400);
        int h = Px(400);
        SheetTiling tiling = SheetTiling.Plan(w, h, Ppm);

        double overlapPx = tiling.OverlapMmValue * Ppm;

        foreach (int row in tiling.Tiles.Select(t => t.Row).Distinct())
        {
            List<SheetTile> inRow = tiling.Tiles.Where(t => t.Row == row).OrderBy(t => t.Col).ToList();
            Assert.Equal(0, inRow[0].SourceRectPx.X);
            Assert.Equal(w, inRow[^1].SourceRectPx.Right);

            for (int i = 1; i < inRow.Count; i++)
            {
                int overlap = inRow[i - 1].SourceRectPx.Right - inRow[i].SourceRectPx.X;
                Assert.True(overlap >= overlapPx - 2, $"row {row}: columns overlap only {overlap}px");
            }
        }

        foreach (int col in tiling.Tiles.Select(t => t.Col).Distinct())
        {
            List<SheetTile> inCol = tiling.Tiles.Where(t => t.Col == col).OrderBy(t => t.Row).ToList();
            Assert.Equal(0, inCol[0].SourceRectPx.Y);
            Assert.Equal(h, inCol[^1].SourceRectPx.Bottom);
        }
    }

    [Fact]
    public void SeamLinesFaceNeighboursOnlyAndSitOnTheOverlapBandEdge()
    {
        SheetTiling tiling = SheetTiling.Plan(Px(400), Px(400), Ppm);

        SheetTile topLeft = tiling.Tiles.Single(t => t is { Row: 1, Col: 1 });
        Assert.NotNull(topLeft.SeamRightMm);
        Assert.NotNull(topLeft.SeamBottomMm);
        Assert.Equal(
            topLeft.PlacedMm.Right - tiling.OverlapMmValue, topLeft.SeamRightMm!.Value, 6);

        SheetTile bottomRight = tiling.Tiles.Single(t => t is { Row: 2, Col: 3 });
        Assert.Null(bottomRight.SeamRightMm);
        Assert.Null(bottomRight.SeamBottomMm);
    }

    [Fact]
    public void TheLabelStripStaysInsideThePrintableArea()
    {
        SheetTiling tiling = SheetTiling.Plan(Px(400), Px(400), Ppm);

        Assert.True(tiling.LabelStripTopMm > tiling.MarginMm);
        Assert.True(tiling.LabelStripTopMm < tiling.PaperHeightMm - tiling.MarginMm);

        foreach (SheetTile tile in tiling.Tiles)
        {
            Assert.True(tile.PlacedMm.Bottom <= tiling.LabelStripTopMm + 1e-6);
        }
    }

    private static int Px(double millimetres) => (int)Math.Round(millimetres * Ppm);
}
