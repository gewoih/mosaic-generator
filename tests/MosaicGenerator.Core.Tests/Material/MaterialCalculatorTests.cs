using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Material;
using MosaicGenerator.Core.Tests.Support;

namespace MosaicGenerator.Core.Tests.Material;

public class MaterialCalculatorTests
{
    [Fact]
    public void ConsumptionGoesFromModuleAreaThroughMassToPrice()
    {
        // 10 x 10 modules of 20 mm with no grout: 100 modules, 0.04 m2 net.
        MosaicLayout layout = RequestFactory.Layout(panelWidth: 200, panelHeight: 200, module: 20, grout: 0);
        Assert.Equal(100, layout.TotalModules);

        Palette palette = PaletteFactory.Of(
            PaletteFactory.Color("#3C6E71", thicknessMm: 8, densityKgPerM3: 2500));
        var plan = new MosaicPlan(layout, palette, new int[layout.TotalModules], seed: 1);

        MaterialReport report = MaterialCalculator.Calculate(plan, wasteFactor: 1.25, pricePerKg: 3200m);

        MaterialLine line = Assert.Single(report.Lines);
        Assert.Equal(100, line.ModuleCount);
        Assert.Equal(0.04, line.NetAreaM2, 1e-12);
        Assert.Equal(0.05, line.GrossAreaM2, 1e-12);

        // 0.05 m2 * 0.008 m * 2500 kg/m3 = 1 kg exactly.
        Assert.Equal(1.0, line.MassKg, 1e-12);
        Assert.Equal(3200m, line.Cost);
    }

    [Fact]
    public void WasteFactorScalesAreaMassAndPriceButNotTheModuleCount()
    {
        MosaicLayout layout = RequestFactory.Layout(panelWidth: 200, panelHeight: 200, module: 20, grout: 0);
        Palette palette = PaletteFactory.Of(PaletteFactory.Color("#3C6E71"));
        var plan = new MosaicPlan(layout, palette, new int[layout.TotalModules], seed: 1);

        MaterialLine none = MaterialCalculator.Calculate(plan, 1.0, 3200m).Lines[0];
        MaterialLine quarter = MaterialCalculator.Calculate(plan, 1.25, 3200m).Lines[0];

        Assert.Equal(none.ModuleCount, quarter.ModuleCount);
        Assert.Equal(none.NetAreaM2, quarter.NetAreaM2, 1e-12);
        Assert.Equal(none.GrossAreaM2 * 1.25, quarter.GrossAreaM2, 1e-12);
        Assert.Equal(none.MassKg * 1.25, quarter.MassKg, 1e-12);
        Assert.Equal(none.Cost * 1.25m, quarter.Cost);
    }

    [Fact]
    public void TotalsAreTheSumOfTheUnroundedLines()
    {
        MosaicLayout layout = RequestFactory.Layout(panelWidth: 200, panelHeight: 200, module: 20, grout: 0);
        Palette palette = PaletteFactory.Of(
            PaletteFactory.Color("#FFFFFF", article: "SM-001", thicknessMm: 8),
            PaletteFactory.Color("#000000", article: "SM-002", thicknessMm: 10),
            PaletteFactory.Color("#FF0000", article: "SM-003", thicknessMm: 6));

        // 60 / 30 / 10 across the grid.
        int[] indices = [.. Enumerable.Repeat(0, 60), .. Enumerable.Repeat(1, 30), .. Enumerable.Repeat(2, 10)];
        var plan = new MosaicPlan(layout, palette, indices, seed: 1);

        MaterialReport report = MaterialCalculator.Calculate(plan, 1.25, 3200m);

        Assert.Equal(100, report.TotalModules);
        Assert.Equal(layout.TotalModules, report.TotalModules);
        Assert.Equal(report.Lines.Sum(l => l.MassKg), report.TotalMassKg, 1e-12);
        Assert.Equal(report.Lines.Sum(l => l.Cost), report.TotalCost);
        Assert.Equal(0.04, report.TotalNetAreaM2, 1e-12);
        Assert.Equal(0.05, report.TotalGrossAreaM2, 1e-12);
    }

    [Fact]
    public void CodesAreAssignedByDescendingConsumption()
    {
        MosaicLayout layout = RequestFactory.Layout(panelWidth: 200, panelHeight: 200, module: 20, grout: 0);
        Palette palette = PaletteFactory.Of(
            PaletteFactory.Color("#FFFFFF", article: "SM-001"),
            PaletteFactory.Color("#000000", article: "SM-002"),
            PaletteFactory.Color("#FF0000", article: "SM-003"));

        int[] indices = [.. Enumerable.Repeat(0, 10), .. Enumerable.Repeat(1, 60), .. Enumerable.Repeat(2, 30)];
        var plan = new MosaicPlan(layout, palette, indices, seed: 1);

        MaterialReport report = MaterialCalculator.Calculate(plan, 1.25, 3200m);

        Assert.Equal(["1", "2", "3"], report.Lines.Select(l => l.Code));
        Assert.Equal(["SM-002", "SM-003", "SM-001"], report.Lines.Select(l => l.Color.Article));

        // The scheme looks colours up by palette index, so the mapping has to survive the sort.
        Assert.True(report.TryGetCode(1, out string code));
        Assert.Equal("1", code);
        Assert.Equal("3", report.LineFor(0).Code);
    }

    [Fact]
    public void UnusedPaletteColoursDoNotAppearInTheReport()
    {
        MosaicLayout layout = RequestFactory.Layout(panelWidth: 200, panelHeight: 200, module: 20, grout: 0);
        Palette palette = PaletteFactory.OfHex("#FFFFFF", "#000000", "#FF0000");
        var plan = new MosaicPlan(layout, palette, new int[layout.TotalModules], seed: 1);

        MaterialReport report = MaterialCalculator.Calculate(plan, 1.25, 3200m);

        Assert.Single(report.Lines);
        Assert.False(report.TryGetCode(1, out _));
    }
}
