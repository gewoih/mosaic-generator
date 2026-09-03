using MosaicGenerator.Core.Domain;

namespace MosaicGenerator.Core.Tests.Domain;

public class ModuleSelectorTests
{
    /// <summary>The plate the user actually works from; the joint that follows is 1 mm.</summary>
    private const double Plate = 7.0;

    [Theory]
    // Panel side mm, chosen bite, expected modules across the short side (plate = 7 mm, grout = 1 mm).
    [InlineData(150, 6, 21)]
    [InlineData(150, 20, 7)]
    [InlineData(600, 10, 54)]
    [InlineData(600, 6, 85)]
    [InlineData(1200, 20, 57)]
    public void ThePieceIsAsLongAsChosenAndAsWideAsThePlate(double sideMm, double along, int expectedAcross)
    {
        ModuleChoice choice = ModuleSelector.Choose(sideMm, sideMm, along, Plate);

        Assert.Equal(along, choice.ModuleAlongMm);
        Assert.Equal(Plate, choice.ModuleAcrossMm);
        Assert.Equal(expectedAcross, choice.ModulesAcrossShortSide);
    }

    [Fact]
    public void ThePieceIsAlwaysExactlyAsWideAcrossItsCourseAsThePlateIsThick()
    {
        // Set cut-face up, the fracture runs through the plate, so that side of the face is the
        // plate's own thickness whatever bite is taken. The bite itself is free to be shorter:
        // a 7 mm plate gives 7x6 pieces readily enough.
        foreach (double plate in new double[] { 4, 6, 7, 10, 15 })
        {
            foreach (double side in new double[] { 150, 300, 600, 1200 })
            {
                foreach (double along in ModuleSelector.AvailableModulesMm)
                {
                    ModuleChoice choice = ModuleSelector.Choose(side, side, along, plate);
                    Assert.Equal(plate, choice.ModuleAcrossMm);
                }
            }
        }
    }

    [Fact]
    public void TheJointFollowsThePlateRatherThanTheBite()
    {
        // The plate is the one dimension the material fixes, so it is what the joint is measured
        // against: the same 7 mm plate gives the same 1 mm joint however long the bite.
        ModuleChoice fine = ModuleSelector.Choose(300, 300, 6, Plate);
        ModuleChoice coarse = ModuleSelector.Choose(1200, 1200, 20, Plate);

        Assert.Equal(ModuleSelector.GroutFor(Plate), fine.GroutMm);
        Assert.Equal(fine.GroutMm, coarse.GroutMm);
        Assert.NotEqual(fine.ModuleAlongMm, coarse.ModuleAlongMm);
    }

    [Fact]
    public void TheBiteMayBeShorterThanThePlateIsThick()
    {
        ModuleChoice choice = ModuleSelector.Choose(150, 150, 6, Plate);

        Assert.Equal(Plate, choice.ModuleAcrossMm);
        Assert.True(
            choice.ModuleAlongMm < choice.ModuleAcrossMm,
            $"bite {choice.ModuleAlongMm} against a {Plate} mm plate");
    }

    [Fact]
    public void TotalModulesIsColumnsTimesRows()
    {
        foreach (double side in new double[] { 150, 300, 600, 1200 })
        {
            foreach (double along in ModuleSelector.AvailableModulesMm)
            {
                ModuleChoice choice = ModuleSelector.Choose(side, side * 1.5, along, Plate);
                int columns = MosaicLayout.FitCount(side, along, choice.GroutMm);
                int rows = MosaicLayout.FitCount(side * 1.5, choice.ModuleAcrossMm, choice.GroutMm);
                Assert.Equal(columns * rows, choice.TotalModules);
            }
        }
    }

    /// <summary>
    /// Choose is a calculator, not a chooser: whatever bite the mosaicist picked comes back
    /// unchanged, however many pieces or however few it turns out to imply. Whether that is
    /// workable is <c>MosaicRequestValidator</c>'s call, not this one's.
    /// </summary>
    [Fact]
    public void ChooseNeitherCapsNorThrowsWhateverTheBiteImplies()
    {
        ModuleChoice tooMany = ModuleSelector.Choose(3000, 3000, 6, Plate);
        Assert.Equal(6, tooMany.ModuleAlongMm);
        Assert.True(tooMany.TotalModules > 40_000);

        ModuleChoice tooFew = ModuleSelector.Choose(150, 150, 20, Plate);
        Assert.Equal(20, tooFew.ModuleAlongMm);
        Assert.True(tooFew.TotalModules > 0);
    }

    [Theory]
    [InlineData(3, 0.7)]
    [InlineData(5, 1.0)]
    [InlineData(6, 1.0)]
    [InlineData(7, 1.0)]
    [InlineData(8, 1.0)]
    [InlineData(10, 1.5)]
    [InlineData(12, 2.0)]
    [InlineData(15, 2.5)]
    [InlineData(20, 3.0)]
    public void TheJointStaysProportionalAndPractical(double module, double grout)
    {
        Assert.Equal(grout, ModuleSelector.GroutFor(module));
    }

    [Fact]
    public void TheJointStaysInsideWhatAHandCanHold()
    {
        foreach (double module in ModuleSelector.AvailableModulesMm)
        {
            Assert.InRange(ModuleSelector.GroutFor(module), 0.7, 3.0);
        }

        // Below about 0.7 mm the piece is being pushed into its neighbour rather than set beside it;
        // above 3 mm the panel reads as adhesive with glass in it.
        Assert.Equal(0.7, ModuleSelector.GroutFor(0.5));
        Assert.Equal(3.0, ModuleSelector.GroutFor(100));
    }
}
