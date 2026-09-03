using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Grid;
using MosaicGenerator.Core.Imaging;
using MosaicGenerator.Core.Rendering;
using MosaicGenerator.Core.Tests.Support;

namespace MosaicGenerator.Core.Tests.Grid;

public class CellSamplerTests
{
    [Fact]
    public void EachCellPicksUpItsOwnRegion()
    {
        // 2x2 modules of 20 mm with a 10 mm grout: step 30, field 50 mm across a 50 px image,
        // so one pixel is one millimetre.
        MosaicLayout layout = RequestFactory.Layout(panelWidth: 50, panelHeight: 50, module: 20, grout: 10);
        Assert.Equal(2, layout.Columns);

        SourceImage image = ImageFactory.FromPixels(50, 50, (x, y) =>
            (x < 25, y < 25) switch
            {
                (true, true) => "#FF0000",
                (false, true) => "#00FF00",
                (true, false) => "#0000FF",
                (false, false) => "#FFFFFF",
            });

        LinearRgb[] cells = CellSampler.Sample(image, new CropRect(0, 0, 50, 50), layout);

        Assert.Equal(4, cells.Length);
        Assert.Equal(Rgb.FromHex("#FF0000").ToBytes(), cells[0].ToSrgb().ToBytes());
        Assert.Equal(Rgb.FromHex("#00FF00").ToBytes(), cells[1].ToSrgb().ToBytes());
        Assert.Equal(Rgb.FromHex("#0000FF").ToBytes(), cells[2].ToSrgb().ToBytes());
        Assert.Equal(Rgb.FromHex("#FFFFFF").ToBytes(), cells[3].ToSrgb().ToBytes());
    }

    [Fact]
    public void TheGroutBandIsNotSampled()
    {
        // Same 2x2 grid; the 10 mm grout band is painted black. A cell that sampled the full
        // step instead of the module footprint would come back visibly darker than white.
        MosaicLayout layout = RequestFactory.Layout(panelWidth: 50, panelHeight: 50, module: 20, grout: 10);

        SourceImage image = ImageFactory.FromPixels(50, 50, (x, y) =>
            x is >= 20 and < 30 || y is >= 20 and < 30 ? "#000000" : "#FFFFFF");

        LinearRgb[] cells = CellSampler.Sample(image, new CropRect(0, 0, 50, 50), layout);

        Assert.All(cells, cell => Assert.Equal(Rgb.FromHex("#FFFFFF").ToBytes(), cell.ToSrgb().ToBytes()));
    }

    [Fact]
    public void SamplingHonoursTheCropOffset()
    {
        MosaicLayout layout = RequestFactory.Layout(panelWidth: 50, panelHeight: 50, module: 20, grout: 10);

        // Left half of a 100x50 image is red, right half green; cropping to the right half
        // must yield green everywhere.
        SourceImage image = ImageFactory.FromPixels(100, 50, (x, _) => x < 50 ? "#FF0000" : "#00FF00");

        LinearRgb[] cells = CellSampler.Sample(image, new CropRect(50, 0, 50, 50), layout);

        Assert.All(cells, cell => Assert.Equal(Rgb.FromHex("#00FF00").ToBytes(), cell.ToSrgb().ToBytes()));
    }

    [Fact]
    public void CellCountMatchesTheLayout()
    {
        MosaicLayout layout = RequestFactory.Layout(panelWidth: 1000, panelHeight: 600, module: 20, grout: 3);
        SourceImage image = ImageFactory.Solid(64, 64, "#808080");

        LinearRgb[] cells = CellSampler.Sample(image, new CropRect(0, 0, 64, 64), layout);

        Assert.Equal(layout.TotalModules, cells.Length);
        Assert.Equal(layout.Columns * layout.Rows, cells.Length);
    }

    [Fact]
    public void ATesseraOnAHardEdgeTakesTheDominantSideNotTheMuddyAverage()
    {
        // Field is 50 mm across a 50 px image, so 1 px = 1 mm. The tessera covers x 10..30;
        // x < 16 is black, the rest white — 30 % black, 70 % white.
        MosaicLayout layout = RequestFactory.Layout(panelWidth: 50, panelHeight: 50, module: 20, grout: 0);
        SourceImage image = ImageFactory.FromPixels(50, 50, (x, _) => x < 16 ? "#000000" : "#FFFFFF");

        LinearRgb[] cells = CellSampler.Sample(
            image, new CropRect(0, 0, 50, 50), layout, [Square(10, 10, 20)]);

        // The plain average would be a dark grey; the dominant side is near white.
        (byte r, _, _) = cells[0].ToSrgb().ToBytes();
        Assert.True(r > 220, $"expected the white side, got R {r}");
    }

    [Fact]
    public void AnEvenlyShadedTesseraStillAverages()
    {
        MosaicLayout layout = RequestFactory.Layout(panelWidth: 50, panelHeight: 50, module: 20, grout: 0);
        SourceImage image = ImageFactory.FromPixels(50, 50, (x, _) => x % 2 == 0 ? "#7C7C7C" : "#848484");

        LinearRgb[] cells = CellSampler.Sample(
            image, new CropRect(0, 0, 50, 50), layout, [Square(10, 10, 20)]);

        (byte r, _, _) = cells[0].ToSrgb().ToBytes();
        Assert.InRange(r, 0x76, 0x8A);
    }

    private static Tessera Square(double x, double y, double size)
    {
        PointD[] polygon = [new(x, y), new(x + size, y), new(x + size, y + size), new(x, y + size)];

        return new Tessera
        {
            Polygon = polygon,
            Centroid = new PointD(x + (size / 2), y + (size / 2)),
            AreaMm2 = size * size,
            CourseId = 0,
            IndexInCourse = 0,
            IsCut = false,
        };
    }
}
