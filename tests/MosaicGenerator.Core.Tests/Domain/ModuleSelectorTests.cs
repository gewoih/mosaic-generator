using MosaicGenerator.Core.Domain;

namespace MosaicGenerator.Core.Tests.Domain;

public class ModuleSelectorTests
{
    private const int ModuleCeiling = 40_000;

    /// <summary>The plate the user actually works from; the joint that follows is 1 mm.</summary>
    private const double Plate = 7.0;

    [Theory]
    // Panel mm, level, expected bite along the course, expected modules across the short side.
    [InlineData(150, DetailLevel.Draft, 6, 21)]
    [InlineData(300, DetailLevel.Draft, 6, 43)]
    [InlineData(300, DetailLevel.Maximum, 6, 43)]
    [InlineData(600, DetailLevel.Draft, 10, 54)]
    [InlineData(600, DetailLevel.Detailed, 6, 85)]
    public void ThePieceIsAsLongAsTheLevelAsksAndAsWideAsThePlate(
        double sideMm, DetailLevel level, double along, int across)
    {
        ModuleChoice choice = ModuleSelector.Choose(sideMm, sideMm, level, ModuleCeiling, Plate);

        Assert.Equal(along, choice.ModuleAlongMm);
        Assert.Equal(Plate, choice.ModuleAcrossMm);
        Assert.Equal(across, choice.ModulesAcrossShortSide);
        Assert.Equal(level.TargetAcross(), choice.RequestedAcross);
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
                foreach (DetailLevel level in Enum.GetValues<DetailLevel>())
                {
                    ModuleChoice choice = ModuleSelector.Choose(side, side, level, ModuleCeiling, plate);
                    Assert.Equal(plate, choice.ModuleAcrossMm);
                }
            }
        }
    }

    [Fact]
    public void AThickPlateStillCapsHowFineTheWorkGoes()
    {
        // Not through the bite — that stays free — but through the courses: they cannot be set
        // closer together than the plate is thick.
        ModuleChoice choice = ModuleSelector.Choose(150, 150, DetailLevel.Detailed, ModuleCeiling, 15);

        Assert.Equal(15, choice.ModuleAcrossMm);
        Assert.Equal(6, choice.ModuleAlongMm);
        Assert.Equal(ModuleLimit.PlateThickness, choice.Limit);
        Assert.False(choice.ReachedTarget);
    }

    [Fact]
    public void TheChosenBiteAlwaysComesFromTheWorkableRange()
    {
        foreach (double side in new double[] { 150, 250, 370, 640, 1130 })
        {
            foreach (DetailLevel level in Enum.GetValues<DetailLevel>())
            {
                ModuleChoice choice = ModuleSelector.Choose(side, side, level, ModuleCeiling, Plate);
                Assert.Contains(choice.ModuleAlongMm, ModuleSelector.AvailableModulesMm);
            }
        }
    }

    [Fact]
    public void TheJointFollowsThePlateRatherThanTheBite()
    {
        // The plate is the one dimension the material fixes, so it is what the joint is measured
        // against: the same 7 mm plate gives the same 1.5 mm joint however long the bite.
        ModuleChoice fine = ModuleSelector.Choose(300, 300, DetailLevel.Maximum, ModuleCeiling, Plate);
        ModuleChoice coarse = ModuleSelector.Choose(1200, 1200, DetailLevel.Draft, ModuleCeiling, Plate);

        Assert.Equal(ModuleSelector.GroutFor(Plate), fine.GroutMm);
        Assert.Equal(fine.GroutMm, coarse.GroutMm);
        Assert.NotEqual(fine.ModuleAlongMm, coarse.ModuleAlongMm);
    }

    [Fact]
    public void ASmallPanelRunsOutOfBiteAndSaysSo()
    {
        // With a plate thin enough to space the courses closely, the range of bites is what runs
        // out first, not the material.
        ModuleChoice choice = ModuleSelector.Choose(200, 200, DetailLevel.Detailed, ModuleCeiling, 3);

        Assert.Equal(6, choice.ModuleAlongMm);
        Assert.Equal(ModuleLimit.PanelTooSmall, choice.Limit);
        Assert.False(choice.ReachedTarget);
    }

    [Fact]
    public void TheBiteMayBeShorterThanThePlateIsThick()
    {
        ModuleChoice choice = ModuleSelector.Choose(150, 150, DetailLevel.Maximum, ModuleCeiling, Plate);

        Assert.Equal(Plate, choice.ModuleAcrossMm);
        Assert.True(
            choice.ModuleAlongMm < choice.ModuleAcrossMm,
            $"bite {choice.ModuleAlongMm} against a {Plate} mm plate");
    }

    [Fact]
    public void OvershootingTheTargetIsNeverAProblem()
    {
        // A 1.2 m panel cannot go coarser than a 20 mm bite, so even draft comes out finely divided.
        ModuleChoice choice = ModuleSelector.Choose(1200, 1200, DetailLevel.Draft, ModuleCeiling, Plate);

        Assert.Equal(20, choice.ModuleAlongMm);
        Assert.True(choice.ModulesAcrossShortSide > choice.RequestedAcross);
        Assert.Equal(ModuleLimit.None, choice.Limit);
    }

    [Fact]
    public void APanelTooLargeForTheCeilingIsReportedRatherThanThrown()
    {
        // With the course width pinned to a 7 mm plate, a 3 m panel needs more pieces than the
        // ceiling allows however long the bite. Saying so beats throwing out of a form.
        ModuleChoice choice = ModuleSelector.Choose(3000, 3000, DetailLevel.Draft, ModuleCeiling, Plate);

        Assert.Equal(ModuleLimit.ModuleCountCapped, choice.Limit);
        Assert.Equal(20, choice.ModuleAlongMm);
        Assert.True(choice.TotalModules > ModuleCeiling);
    }

    [Fact]
    public void ATightCeilingForcesACoarserBiteAndIsReported()
    {
        ModuleChoice choice = ModuleSelector.Choose(600, 600, DetailLevel.Detailed, maxModules: 3000, 6);

        Assert.True(choice.TotalModules <= 3000, $"got {choice.TotalModules}");
        Assert.True(choice.ModulesAcrossShortSide < choice.RequestedAcross);
        Assert.Equal(ModuleLimit.ModuleCountCapped, choice.Limit);
    }

    [Fact]
    public void NoLevelOnAnyPanelEverExceedsTheCeiling()
    {
        foreach (double width in new double[] { 150, 300, 600, 1200, 2000, 3000 })
        {
            foreach (double height in new double[] { 150, 300, 600, 1200, 2000, 3000 })
            {
                if (Math.Max(width, height) / Math.Min(width, height) > 5.0)
                {
                    continue;
                }

                foreach (DetailLevel level in Enum.GetValues<DetailLevel>())
                {
                    ModuleChoice choice =
                        ModuleSelector.Choose(width, height, level, ModuleCeiling, Plate);
                    Assert.True(
                        choice.TotalModules <= ModuleCeiling
                            || choice.Limit == ModuleLimit.ModuleCountCapped,
                        $"{width}x{height} {level}: {choice.TotalModules}");
                }
            }
        }
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

    [Fact]
    public void EachLevelAsksForMoreModulesThanTheLast()
    {
        int[] targets = [.. Enum.GetValues<DetailLevel>().Select(l => l.TargetAcross())];

        Assert.Equal(targets.OrderBy(t => t), targets);
        Assert.Equal(targets.Distinct().Count(), targets.Length);
    }
}
