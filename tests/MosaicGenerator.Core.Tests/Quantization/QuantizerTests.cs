using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Quantization;
using MosaicGenerator.Core.Tests.Support;

namespace MosaicGenerator.Core.Tests.Quantization;

public class QuantizerTests
{
    [Fact]
    public void AColourThatIsInThePaletteMapsToItself()
    {
        Palette palette = PaletteFactory.OfHex("#FFFFFF", "#000000", "#3C6E71", "#B33951");

        LinearRgb[] cells =
        [
            Rgb.FromHex("#3C6E71").ToLinear(),
            Rgb.FromHex("#000000").ToLinear(),
            Rgb.FromHex("#B33951").ToLinear(),
            Rgb.FromHex("#FFFFFF").ToLinear(),
        ];

        Assert.Equal([2, 1, 3, 0], Quantizer.Map(cells, palette));
    }

    [Fact]
    public void NearestIsMeasuredInLabNotInRgb()
    {
        // Against these two candidates, #508FE0 sits 8.4 dE from the second and 22.9 dE from the
        // first, while plain Euclidean RGB distance ranks them the other way round.
        Palette palette = PaletteFactory.OfHex("#59A0CD", "#077FCF");
        LinearRgb target = Rgb.FromHex("#508FE0").ToLinear();

        int[] mapped = Quantizer.Map([target], palette);

        Assert.Equal(1, mapped[0]);
        Assert.Equal(0, NearestInRgb(Rgb.FromHex("#508FE0"), palette));
    }

    [Fact]
    public void EveryCellGetsAValidPaletteIndex()
    {
        Palette palette = PaletteFactory.OfHex("#FFFFFF", "#000000", "#FF0000");
        LinearRgb[] cells = [.. Enumerable.Range(0, 50)
            .Select(i => new LinearRgb(i / 49.0, 1 - (i / 49.0), 0.5))];

        int[] mapped = Quantizer.Map(cells, palette);

        Assert.Equal(cells.Length, mapped.Length);
        Assert.All(mapped, index => Assert.InRange(index, 0, palette.Colors.Count - 1));
    }

    [Fact]
    public void TiesResolveToTheFirstCandidateSoTheResultIsStable()
    {
        Palette palette = PaletteFactory.OfHex("#808080", "#808080");

        Assert.Equal(0, Quantizer.Map([Rgb.FromHex("#808080").ToLinear()], palette)[0]);
    }

    private static int NearestInRgb(Rgb target, Palette palette)
    {
        int best = 0;
        double bestDistance = double.MaxValue;

        for (int i = 0; i < palette.Colors.Count; i++)
        {
            Rgb candidate = palette.Colors[i].Rgb;
            double distance =
                Square(target.R - candidate.R) + Square(target.G - candidate.G) + Square(target.B - candidate.B);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;

        static double Square(double value) => value * value;
    }
}
