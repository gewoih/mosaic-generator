using MosaicGenerator.Core.Colors;

namespace MosaicGenerator.Core.Tests.Colors;

public class ColorDistanceTests
{
    // Sharma, Wu & Dalal (2005), Table 1 — the reference dataset every CIEDE2000
    // implementation is checked against. L, a, b for each side, then the published ΔE00.
    [Theory]
    [InlineData(50.0, 2.6772, -79.7751, 50.0, 0.0, -82.7485, 2.0425)]
    [InlineData(50.0, 3.1571, -77.2803, 50.0, 0.0, -82.7485, 2.8615)]
    [InlineData(50.0, 2.8361, -74.0200, 50.0, 0.0, -82.7485, 3.4412)]
    [InlineData(50.0, -1.3802, -84.2814, 50.0, 0.0, -82.7485, 1.0000)]
    [InlineData(50.0, 0.0, 0.0, 50.0, -1.0, 2.0, 2.3669)]
    [InlineData(50.0, 2.4900, -0.0010, 50.0, -2.4900, 0.0009, 7.1792)]
    [InlineData(50.0, -0.0010, 2.4900, 50.0, 0.0009, -2.4900, 4.8045)]
    [InlineData(50.0, 2.5, 0.0, 73.0, 25.0, -18.0, 27.1492)]
    [InlineData(50.0, 2.5, 0.0, 61.0, -5.0, 29.0, 22.8977)]
    [InlineData(50.0, 2.5, 0.0, 50.0, 3.2592, 0.3350, 1.0000)]
    [InlineData(60.2574, -34.0099, 36.2677, 60.4626, -34.1751, 39.4387, 1.2644)]
    [InlineData(35.0831, -44.1164, 3.7933, 35.0232, -40.0716, 1.5901, 1.8645)]
    [InlineData(22.7233, 20.0904, -46.6940, 23.0331, 14.9730, -42.5619, 2.0373)]
    [InlineData(6.7747, -0.2908, -2.4247, 5.8714, -0.0985, -2.2286, 0.6377)]
    public void Ciede2000MatchesTheSharmaReferenceDataset(
        double l1, double a1, double b1, double l2, double a2, double b2, double expected)
    {
        double delta = ColorDistance.CieDe2000(new CieLab(l1, a1, b1), new CieLab(l2, a2, b2));

        Assert.Equal(expected, delta, 1e-4);
    }

    [Fact]
    public void Ciede2000IsSymmetric()
    {
        var a = new CieLab(22.7233, 20.0904, -46.6940);
        var b = new CieLab(23.0331, 14.9730, -42.5619);

        Assert.Equal(ColorDistance.CieDe2000(a, b), ColorDistance.CieDe2000(b, a), 1e-12);
    }

    [Fact]
    public void Ciede2000IsZeroForAColourAgainstItself()
    {
        var c = new CieLab(41.0, -13.0, 27.0);

        Assert.Equal(0.0, ColorDistance.CieDe2000(c, c), 1e-12);
    }

    [Fact]
    public void MatchingMetricDefaultsToCie76()
    {
        Assert.Equal(ColorDistance.Metric.Cie76, ColorDistance.MatchingMetric);
    }

    [Fact]
    public void MatchUnderCie76IsTheHueWeightedFormAndMatchSquaredItsSquare()
    {
        var a = new CieLab(50.0, 10.0, -20.0);
        var b = new CieLab(55.0, -4.0, 12.0);

        Assert.Equal(ColorDistance.Metric.Cie76, ColorDistance.MatchingMetric);
        Assert.Equal(
            Math.Sqrt(ColorDistance.HueWeightedSquared(a, b)), ColorDistance.Match(a, b), 1e-12);
        Assert.Equal(ColorDistance.HueWeightedSquared(a, b), ColorDistance.MatchSquared(a, b), 1e-12);
    }

    [Fact]
    public void ColoursOfOneHueAreMeasuredExactlyAsCie76Does()
    {
        // Same hue angle, different chroma and lightness: there is no hue term to weight, so the
        // weighting may not touch the figure at all.
        var a = new CieLab(50.0, 12.0, 16.0);
        var b = new CieLab(58.0, 24.0, 32.0);

        Assert.Equal(ColorDistance.CieDe76Squared(a, b), ColorDistance.HueWeightedSquared(a, b), 1e-9);
    }

    [Fact]
    public void AChangeOfHueCostsMoreThanAnEqualLossOfChroma()
    {
        // Two candidates the same euclidean distance from the target: one duller, one turned. The
        // turned one has to cost more — that is the whole of the hue penalty.
        var target = new CieLab(50.0, 30.0, 0.0);
        var duller = new CieLab(50.0, 10.0, 0.0);
        // Turned far enough that the chord it travels is the same 20 units the duller one lost.
        double turn = 2.0 * Math.Asin(10.0 / 30.0);
        var turned = new CieLab(50.0, 30.0 * Math.Cos(turn), 30.0 * Math.Sin(turn));

        Assert.Equal(
            ColorDistance.CieDe76Squared(target, duller),
            ColorDistance.CieDe76Squared(target, turned),
            0.5);
        Assert.True(
            ColorDistance.HueWeightedSquared(target, turned)
                > ColorDistance.HueWeightedSquared(target, duller) * 2.0,
            $"turned {ColorDistance.HueWeightedSquared(target, turned):0.0} against "
                + $"duller {ColorDistance.HueWeightedSquared(target, duller):0.0}");
    }

    [Fact]
    public void ANeutralIsNotChargedForHavingTheWrongHue()
    {
        // Grey has no hue to be wrong about: two near-neutrals on opposite sides of the axis must
        // stay as close as CIE76 says they are, whatever the hue angle between them.
        var a = new CieLab(60.0, 1.0, 1.0);
        var b = new CieLab(60.0, -1.0, -1.0);

        Assert.True(
            ColorDistance.HueWeightedSquared(a, b) < ColorDistance.CieDe76Squared(a, b) * 3.5,
            $"{ColorDistance.HueWeightedSquared(a, b):0.00} against CIE76 "
                + $"{ColorDistance.CieDe76Squared(a, b):0.00}");
        Assert.True(Math.Sqrt(ColorDistance.HueWeightedSquared(a, b)) < 5.0);
    }

    [Fact]
    public void MatchFollowsTheMetricSwitch()
    {
        var a = new CieLab(32.0, 79.0, -107.0);
        var b = new CieLab(30.0, 68.0, -98.0);

        try
        {
            ColorDistance.MatchingMetric = ColorDistance.Metric.Ciede2000;

            Assert.Equal(ColorDistance.CieDe2000(a, b), ColorDistance.Match(a, b), 1e-12);
            double d = ColorDistance.CieDe2000(a, b);
            Assert.Equal(d * d, ColorDistance.MatchSquared(a, b), 1e-12);
            // In the blue region CIEDE2000 pulls the distance well below the 1976 figure.
            Assert.True(ColorDistance.CieDe2000(a, b) < ColorDistance.CieDe76(a, b));
        }
        finally
        {
            ColorDistance.MatchingMetric = ColorDistance.Metric.Cie76;
        }
    }
}
