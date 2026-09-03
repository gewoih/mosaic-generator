using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Optics;
using MosaicGenerator.Core.Quantization;
using MosaicGenerator.Core.Tests.Support;

namespace MosaicGenerator.Core.Tests.Quantization;

public class JointCompensationTests
{
    // 4 mm tesserae on a 1 mm joint: 64% glass, and the rest is adhesive down a deep slot.
    private static readonly JointOptics Joint = JointOptics.For(4, 4, 1, 8);

    [Fact]
    public void MatchingThroughTheJointReachesForALighterArticle()
    {
        Palette palette = PaletteFactory.OfHex("#606060", "#808080", "#909090", "#A0A0A0", "#C0C0C0");
        CieLab[] cell = Quantizer.ToLab([Rgb.FromHex("#808080").ToLinear()]);

        int naive = Quantizer.Map(cell, PaletteObservation.Lab(palette))[0];
        int compensated = Quantizer.Map(cell, PaletteObservation.Lab(palette, Joint))[0];

        // Taken on its own, the mid grey is the obvious match.
        Assert.Equal("#808080", palette.Colors[naive].Hex);

        // This target sits above the joint's own lightness, so setting it into the joint would
        // drag it down; a lighter shade is the one that actually reads as mid grey on the wall.
        Assert.True(
            palette.Colors[compensated].Lab.L > palette.Colors[naive].Lab.L,
            $"compensated pick {palette.Colors[compensated].Hex} should be lighter than {palette.Colors[naive].Hex}");
    }

    [Fact]
    public void ForAShadeDarkerThanTheJointTheCorrectionGoesTheOtherWay()
    {
        // The joint sits near L* 37. Below that it is the joint that lightens the cell, so
        // holding a dark tone calls for a darker article, not a lighter one. Treating the joint
        // as a uniform darkening would get this backwards.
        Palette palette = PaletteFactory.OfHex(
            "#101010", "#303030", "#505050", "#707070", "#909090", "#B0B0B0", "#D0D0D0", "#F0F0F0");
        CieLab[] cell = Quantizer.ToLab([Rgb.FromHex("#4A4A4A").ToLinear()]);

        int naive = Quantizer.Map(cell, PaletteObservation.Lab(palette))[0];
        int compensated = Quantizer.Map(cell, PaletteObservation.Lab(palette, Joint))[0];

        Assert.True(
            palette.Colors[compensated].Lab.L < palette.Colors[naive].Lab.L,
            $"{palette.Colors[compensated].Hex} should be darker than the naive {palette.Colors[naive].Hex}");
    }

    [Fact]
    public void CompensationAlwaysPushesAwayFromTheJointNeverToward()
    {
        Palette palette = PaletteFactory.OfHex(
            "#101010", "#303030", "#505050", "#707070", "#909090", "#B0B0B0", "#D0D0D0", "#F0F0F0");

        CieLab[] plain = PaletteObservation.Lab(palette);
        CieLab[] observed = PaletteObservation.Lab(palette, Joint);
        double jointL = Joint.JointLinear.ToLab().L;

        foreach (string hex in new[] { "#4A4A4A", "#7C7C7C", "#A6A6A6", "#DDDDDD" })
        {
            CieLab[] cell = Quantizer.ToLab([Rgb.FromHex(hex).ToLinear()]);

            double naive = palette.Colors[Quantizer.Map(cell, plain)[0]].Lab.L;
            double compensated = palette.Colors[Quantizer.Map(cell, observed)[0]].Lab.L;

            Assert.True(
                Math.Abs(compensated - jointL) >= Math.Abs(naive - jointL) - 1e-9,
                $"{hex}: corrected pick L* {compensated:0.0} should sit no closer to the joint "
                + $"at {jointL:0.0} than the naive {naive:0.0}");
        }
    }

    [Fact]
    public void AWideJointBesideALargeTesseraBarelyMovesTheChoice()
    {
        // 20 mm module on a 3 mm joint: three quarters glass, and the slot is open enough to stay
        // mid grey, so the correction is small.
        JointOptics coarse = JointOptics.For(20, 20, 3, 8);
        Palette palette = PaletteFactory.OfHex("#606060", "#808080", "#909090", "#A0A0A0");
        CieLab[] cell = Quantizer.ToLab([Rgb.FromHex("#8A8A8A").ToLinear()]);

        int fine = Quantizer.Map(cell, PaletteObservation.Lab(palette, Joint))[0];
        int wide = Quantizer.Map(cell, PaletteObservation.Lab(palette, coarse))[0];

        Assert.True(
            palette.Colors[fine].Lab.L >= palette.Colors[wide].Lab.L,
            "the narrower joint needs at least as much correction as the wider one");
    }
}
