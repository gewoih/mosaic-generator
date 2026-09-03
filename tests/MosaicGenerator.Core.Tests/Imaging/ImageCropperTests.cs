using MosaicGenerator.Core.Imaging;

namespace MosaicGenerator.Core.Tests.Imaging;

public class ImageCropperTests
{
    [Theory]
    // Source wider than the target: full height kept, sides trimmed evenly.
    [InlineData(200, 100, 1.0, 50, 0, 100, 100)]
    // Source taller than the target: full width kept, top and bottom trimmed evenly.
    [InlineData(100, 200, 1.0, 0, 50, 100, 100)]
    // Aspect already matches: nothing trimmed.
    [InlineData(300, 200, 1.5, 0, 0, 300, 200)]
    [InlineData(100, 100, 2.0, 0, 25, 100, 50)]
    [InlineData(100, 100, 0.5, 25, 0, 50, 100)]
    public void CentreCropMatchesTheTargetAspect(
        int width, int height, double aspect, int x, int y, int cropWidth, int cropHeight)
    {
        CropRect crop = ImageCropper.CenterCropToAspect(width, height, aspect);

        Assert.Equal(new CropRect(x, y, cropWidth, cropHeight), crop);
    }

    [Fact]
    public void CropNeverLeavesTheSourceBounds()
    {
        foreach (double aspect in new[] { 0.2, 0.5, 1.0, 2.0, 5.0 })
        {
            CropRect crop = ImageCropper.CenterCropToAspect(137, 91, aspect);

            Assert.True(crop.X >= 0 && crop.Y >= 0);
            Assert.True(crop.X + crop.Width <= 137);
            Assert.True(crop.Y + crop.Height <= 91);
            Assert.True(crop.Width > 0 && crop.Height > 0);
        }
    }

    [Theory]
    // A 200x100 source cropped square: the window is 100 wide and slides across 100 px of travel.
    [InlineData(0.0, 0)]
    [InlineData(0.25, 0)]
    [InlineData(0.5, 50)]
    [InlineData(0.75, 100)]
    [InlineData(1.0, 100)]
    public void TheAnchorSlidesTheWindowAndStopsAtTheEdges(double anchorX, int expectedX)
    {
        CropRect crop = ImageCropper.CropToAspect(200, 100, 1.0, anchorX, 0.5);

        Assert.Equal(expectedX, crop.X);
        Assert.Equal(new CropRect(expectedX, 0, 100, 100), crop);
    }

    [Fact]
    public void TheDefaultAnchorIsTheCentreCrop()
    {
        foreach ((int width, int height, double aspect) in
                 new[] { (200, 100, 1.0), (137, 91, 0.7), (640, 480, 2.0), (100, 100, 1.0) })
        {
            Assert.Equal(
                ImageCropper.CenterCropToAspect(width, height, aspect),
                ImageCropper.CropToAspect(width, height, aspect, 0.5, 0.5));
        }
    }

    [Fact]
    public void AnAnchorAnywhereKeepsTheWindowInsideTheSource()
    {
        foreach (double aspect in new[] { 0.2, 0.5, 1.0, 2.0, 5.0 })
        {
            foreach (double anchor in new[] { -1.0, 0.0, 0.3, 0.5, 0.9, 1.0, 2.0, double.NaN })
            {
                CropRect crop = ImageCropper.CropToAspect(137, 91, aspect, anchor, 1 - anchor);

                Assert.True(crop.X >= 0 && crop.Y >= 0, $"aspect {aspect}, anchor {anchor}");
                Assert.True(crop.X + crop.Width <= 137);
                Assert.True(crop.Y + crop.Height <= 91);
                Assert.True(crop.Width > 0 && crop.Height > 0);
            }
        }
    }

    [Fact]
    public void MovingTheAnchorChangesWhichPartOfThePhotographSurvives()
    {
        // A head near the top of an upright frame is cut off by a centre crop and kept by a high
        // anchor, which is the whole reason the anchor exists.
        CropRect centred = ImageCropper.CropToAspect(300, 400, 1.0, 0.5, 0.5);
        CropRect high = ImageCropper.CropToAspect(300, 400, 1.0, 0.5, 0.0);

        Assert.Equal(0, high.Y);
        Assert.True(centred.Y > high.Y);
        Assert.Equal(centred.Width, high.Width);
        Assert.Equal(centred.Height, high.Height);
    }
}
