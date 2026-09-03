using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Optics;
using MosaicGenerator.Core.Tests.Support;

namespace MosaicGenerator.Core.Tests.Optics;

public class JointAppearanceTests
{
    [Fact]
    public void AJointFlushWithTheFaceIsNotShadedAtAll()
    {
        Assert.Equal(1.0, JointAppearance.Occlusion(slotWidthMm: 2, depthMm: 0));
    }

    [Theory]
    // sqrt(1 + r²) - r for r = depth / width.
    [InlineData(1, 1, 0.41421)]
    [InlineData(1, 4, 0.12311)]
    [InlineData(2, 4, 0.23607)]
    [InlineData(3, 4, 0.33333)]
    public void ShadingFollowsTheGrooveFormFactor(double width, double depth, double expected)
    {
        Assert.Equal(expected, JointAppearance.Occlusion(width, depth), 1e-5);
    }

    [Fact]
    public void ANarrowerSlotOfTheSameDepthLetsInLessLight()
    {
        double[] widths = [0.5, 1, 2, 3, 5, 10];

        double previous = 0;
        foreach (double width in widths)
        {
            double occlusion = JointAppearance.Occlusion(width, depthMm: 4);
            Assert.True(occlusion > previous, $"width {width} should open up, got {occlusion}");
            previous = occlusion;
        }
    }

    [Fact]
    public void WhiteAdhesiveDownARealJointReadsAsGreyNotWhite()
    {
        // 4 mm module, 1 mm joint, 8 mm smalt: the slot is four times deeper than it is wide.
        double lightness = JointAppearance.JointColor(groutWidthMm: 1, tesseraThicknessMm: 8).ToLab().L;

        Assert.InRange(lightness, 30, 45);

        // The same adhesive in the open is far brighter; the slot is what does the work.
        Assert.InRange(JointAppearance.AdhesiveWhite.ToLab().L, 88, 94);
    }

    [Fact]
    public void TheModuleFractionIsTheShareOfTheCellThatIsActuallyTessera()
    {
        // 3 mm tesserae on a 1 mm joint: barely more than half the panel is glass.
        JointOptics fine = JointOptics.For(3, 3, 1, 8);
        Assert.Equal(9.0 / 16.0, fine.ModuleFraction, 1e-9);

        JointOptics coarse = JointOptics.For(20, 20, 3, 8);
        Assert.Equal(400.0 / 529.0, coarse.ModuleFraction, 1e-9);
    }

    [Fact]
    public void MixingInTheJointPullsEveryShadeTowardTheJointsOwnLightness()
    {
        // The joint is not a shadow cast over the work: it is a grey of its own, around L* 37 for
        // a 1 mm slot. Shades above it come down, shades below it come up, and the panel ends up
        // with less range than the palette had.
        JointOptics joint = JointOptics.For(4, 4, 1, 8);
        double jointL = joint.JointLinear.ToLab().L;

        foreach (string hex in new[] { "#FFFFFF", "#B0B0B0", "#808080", "#3C6E71", "#303030", "#000000" })
        {
            double shade = Rgb.FromHex(hex).ToLab().L;
            double observed = joint.Observe(Rgb.FromHex(hex).ToLinear()).ToLab().L;

            Assert.True(
                Math.Abs(observed - jointL) < Math.Abs(shade - jointL) + 1e-9,
                $"{hex}: L* {shade:0.0} -> {observed:0.0} should move toward the joint at {jointL:0.0}");
        }
    }

    [Fact]
    public void TheJointCompressesTheRangeSoTheDeepestShadowIsUnreachable()
    {
        JointOptics joint = JointOptics.For(4, 4, 1, 8);

        // Even pure black glass cannot read black once a third of its cell is lit adhesive.
        double blackOnTheWall = joint.Observe(Rgb.FromHex("#000000").ToLinear()).ToLab().L;
        double whiteOnTheWall = joint.Observe(Rgb.FromHex("#FFFFFF").ToLinear()).ToLab().L;

        Assert.InRange(blackOnTheWall, 18, 28);
        Assert.True(whiteOnTheWall < 95, "and the highlight comes down too");
        Assert.True(whiteOnTheWall - blackOnTheWall < 100 - 0, "the panel has less range than the palette");
    }

    [Fact]
    public void ATesseraWithNoJointAroundItIsShownAsItself()
    {
        JointOptics none = JointOptics.For(10, 10, groutWidthMm: 0, tesseraThicknessMm: 8);
        LinearRgb shade = Rgb.FromHex("#B33951").ToLinear();

        Assert.Equal(1.0, none.ModuleFraction, 1e-12);
        Assert.Equal(shade.ToLab().L, none.Observe(shade).ToLab().L, 1e-9);
    }

    [Fact]
    public void TheJointATestPaletteImpliesTracksTheModule()
    {
        double fine = JointOptics.For(PaletteFactory.Layout(module: 4, grout: 1), 8).JointRgb.ToLab().L;
        double coarse = JointOptics.For(PaletteFactory.Layout(module: 20, grout: 3), 8).JointRgb.ToLab().L;

        Assert.True(fine < coarse - 10, $"fine {fine:0.0} should sit well under coarse {coarse:0.0}");
    }
}
