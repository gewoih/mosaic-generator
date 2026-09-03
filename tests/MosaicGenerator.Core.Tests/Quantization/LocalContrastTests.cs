using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Grid;
using MosaicGenerator.Core.Quantization;
using MosaicGenerator.Core.Rendering;

namespace MosaicGenerator.Core.Tests.Quantization;

public class LocalContrastTests
{
    [Fact]
    public void AFlatFieldIsLeftExactlyAsItWas()
    {
        // A wall of one colour. There is nothing to separate, and inventing separation here is the
        // speckle failure of docs/krap-tona-plan.md all over again.
        CieLab sky = new(70, 0, -20);
        CieLab[] cells = [.. Enumerable.Repeat(sky, 12)];

        CieLab[] lifted = LocalContrast.Lift(cells, Neighbourhood(12, spacing: 10, reach: 25));

        Assert.All(lifted, c => Assert.Equal(0.0, ColorDistance.CieDe76(c, sky), 9));
    }

    [Fact]
    public void TextureBelowTheDeadBandIsNotAmplified()
    {
        // Grain, ripple, the encoder's own noise: a couple of ΔE of wobble on an otherwise flat sky.
        // Whatever survives the smoothing pass before this one must not be multiplied here.
        var random = new Random(7);
        CieLab[] cells =
        [
            .. Enumerable.Range(0, 40).Select(_ =>
                new CieLab(70 + ((random.NextDouble() - 0.5) * 2.0), 0, -20)),
        ];

        CieLab[] lifted = LocalContrast.Lift(cells, Neighbourhood(40, spacing: 10, reach: 25));

        for (int i = 0; i < cells.Length; i++)
        {
            Assert.True(
                ColorDistance.CieDe76(cells[i], lifted[i]) < 1e-9,
                $"cell {i} moved by {ColorDistance.CieDe76(cells[i], lifted[i]):0.000} ΔE");
        }
    }

    [Fact]
    public void TwoPlanesTheCameraBarelySeparatedComeApart()
    {
        // The whole point: a hazy ridge against the sky behind it, ten ΔE apart — still under one
        // and a half of the 7,1 ΔE step a twelve-shade work has. After the lift they must stand
        // further apart than the camera left them.
        CieLab sky = new(70, 0, -20);
        CieLab ridge = new(60, 0, -20);
        CieLab[] cells = [.. Enumerable.Range(0, 20).Select(i => i < 10 ? sky : ridge)];

        CieLab[] lifted = LocalContrast.Lift(cells, Neighbourhood(20, spacing: 10, reach: 25));

        double before = ColorDistance.CieDe76(cells[9], cells[10]);
        double after = ColorDistance.CieDe76(lifted[9], lifted[10]);

        Assert.True(after > before, $"{before:0.0} ΔE → {after:0.0} ΔE");
        Assert.True(lifted[9].L > lifted[10].L, "the lighter plane must stay the lighter one");
    }

    [Fact]
    public void OrderOfTonesIsNeverTurnedOver()
    {
        // Lifting may move the boundary between two planes; it may not put the dark one on top.
        // A negative on a cartoon is not a contrast problem, it is a different picture.
        CieLab[] cells =
        [
            .. Enumerable.Range(0, 25).Select(i => new CieLab(30 + (i * 2.0), 0, 0)),
        ];

        CieLab[] lifted = LocalContrast.Lift(cells, Neighbourhood(25, spacing: 10, reach: 25));

        for (int i = 1; i < cells.Length; i++)
        {
            Assert.True(
                lifted[i].L >= lifted[i - 1].L - 1e-9,
                $"ramp turned over between {i - 1} and {i}");
        }
    }

    [Fact]
    public void AHardSilhouetteIsCappedRatherThanHaloed()
    {
        // A dark fir against a bright sky is already separated by far more than a tonal step.
        // Lifting it further buys nothing and would ring a bright collar around the tree.
        CieLab dark = new(20, 0, 0);
        CieLab bright = new(90, 0, 0);
        CieLab[] cells = [.. Enumerable.Range(0, 20).Select(i => i < 10 ? dark : bright)];

        CieLab[] lifted = LocalContrast.Lift(cells, Neighbourhood(20, spacing: 10, reach: 25));

        for (int i = 0; i < cells.Length; i++)
        {
            Assert.True(
                ColorDistance.CieDe76(cells[i], lifted[i]) <= LocalContrast.MostLiftDeltaE + 1e-9,
                $"cell {i} moved {ColorDistance.CieDe76(cells[i], lifted[i]):0.0} ΔE");
        }
    }

    [Fact]
    public void ColourIsLiftedAndNotOnlyLightness()
    {
        // Haze drains colour as well as tone: the far ridge is greyer, not only paler. Planes the
        // camera separated by chroma alone must come apart too.
        CieLab near = new(60, 16, 8);
        CieLab far = new(60, 4, 2);
        CieLab[] cells = [.. Enumerable.Range(0, 20).Select(i => i < 10 ? near : far)];

        CieLab[] lifted = LocalContrast.Lift(cells, Neighbourhood(20, spacing: 10, reach: 25));

        Assert.True(
            ColorDistance.CieDe76(lifted[9], lifted[10]) > ColorDistance.CieDe76(cells[9], cells[10]));
    }

    [Fact]
    public void TheSamePictureLiftsTheSameOnAnyPanelSize()
    {
        // The radius is in modules, not pixels. The identical subject on a 15×15 and on a 30×30
        // panel must be lifted identically, or the correction is a different one per panel.
        CieLab[] cells = [.. Enumerable.Range(0, 20).Select(i => new CieLab(i < 10 ? 70 : 58, 0, -18))];

        CieLab[] small = LocalContrast.Lift(cells, Neighbourhood(20, spacing: 7, reach: 17.5));
        CieLab[] large = LocalContrast.Lift(cells, Neighbourhood(20, spacing: 14, reach: 35.0));

        for (int i = 0; i < cells.Length; i++)
        {
            Assert.Equal(0.0, ColorDistance.CieDe76(small[i], large[i]), 9);
        }
    }

    [Fact]
    public void WithNoNeighboursNothingMoves()
    {
        CieLab[] cells = [.. Enumerable.Range(0, 6).Select(i => new CieLab(i * 15.0, 5, -5))];

        CieLab[] lifted = LocalContrast.Lift(cells, Neighbourhood(6, spacing: 100, reach: 1));

        Assert.All(
            Enumerable.Range(0, cells.Length),
            i => Assert.Equal(0.0, ColorDistance.CieDe76(cells[i], lifted[i]), 9));
    }

    [Fact]
    public void AnEmptyFieldIsNotAFailure()
    {
        Assert.Empty(LocalContrast.Lift([], Neighbourhood(0, spacing: 10, reach: 25)));
    }

    private static CellNeighbourhood Neighbourhood(int count, double spacing, double reach) =>
        CellNeighbourhood.Build(LineOf(count, spacing), reach);

    private static Tessera[] LineOf(int count, double spacing) =>
        [.. Enumerable.Range(0, count).Select(i => new Tessera
        {
            Polygon = [new(i * spacing, 0), new((i * spacing) + 8, 0), new((i * spacing) + 8, 8), new(i * spacing, 8)],
            Centroid = new PointD(i * spacing, 4),
            AreaMm2 = 64,
            CourseId = 0,
            IndexInCourse = i,
            IsCut = false,
        })];
}
