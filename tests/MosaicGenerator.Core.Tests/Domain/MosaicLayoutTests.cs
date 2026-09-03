using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Tests.Support;

namespace MosaicGenerator.Core.Tests.Domain;

public class MosaicLayoutTests
{
    [Fact]
    public void LeftoverBecomesAMarginAndTheRequestedDimensionsAreUntouched()
    {
        MosaicLayout layout = RequestFactory.Layout(panelWidth: 1000, panelHeight: 1000, module: 20, grout: 3);

        // step = 23; (1000 + 3) / 23 = 43.6 -> 43 modules spanning 43 * 23 - 3 = 986 mm.
        Assert.Equal(43, layout.Columns);
        Assert.Equal(43, layout.Rows);
        Assert.Equal(986.0, layout.FieldWidthMm, 1e-9);
        Assert.Equal(7.0, layout.MarginXMm, 1e-9);
        Assert.Equal(20.0, layout.ModuleWidthMm, 1e-9);
        Assert.Equal(3.0, layout.GroutWidthMm, 1e-9);
        Assert.Equal(1000.0, layout.PanelWidthMm, 1e-9);
    }

    [Fact]
    public void AnExactFitLeavesNoMargin()
    {
        MosaicLayout layout = RequestFactory.Layout(panelWidth: 986, panelHeight: 986, module: 20, grout: 3);

        Assert.Equal(43, layout.Columns);
        Assert.Equal(0.0, layout.MarginXMm, 1e-9);
        Assert.Equal(986.0, layout.FieldWidthMm, 1e-9);
    }

    [Theory]
    // A single module needs the module itself; the trailing grout is never laid.
    [InlineData(23, 20, 3, 1)]
    [InlineData(20, 20, 3, 1)]
    [InlineData(19.999, 20, 3, 0)]
    [InlineData(46, 20, 3, 2)]
    [InlineData(100, 10, 0, 10)]
    public void FitCountAccountsForTheMissingTrailingGrout(
        double panel, double module, double grout, int expected)
    {
        Assert.Equal(expected, MosaicLayout.FitCount(panel, module, grout));
    }

    [Fact]
    public void AModuleLargerThanThePanelIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            RequestFactory.Layout(panelWidth: 15, panelHeight: 1000, module: 20, grout: 3));
    }

    [Fact]
    public void ModuleOriginsWalkTheGridInStepsOfModulePlusGrout()
    {
        MosaicLayout layout = RequestFactory.Layout(panelWidth: 1000, panelHeight: 1000, module: 20, grout: 3);

        Assert.Equal(7.0, layout.ModuleLeftMm(0), 1e-9);
        Assert.Equal(30.0, layout.ModuleLeftMm(1), 1e-9);
        Assert.Equal(7.0 + (42 * 23.0), layout.ModuleLeftMm(42), 1e-9);

        // The last module ends flush with the far margin.
        Assert.Equal(1000.0 - 7.0, layout.ModuleLeftMm(42) + 20.0, 1e-9);
    }

    [Fact]
    public void FieldAspectDrivesTheCropNotThePanelAspect()
    {
        MosaicLayout layout = RequestFactory.Layout(panelWidth: 1000, panelHeight: 500, module: 20, grout: 3);

        Assert.NotEqual(layout.PanelWidthMm / layout.PanelHeightMm, layout.FieldAspect, 1e-6);
        Assert.Equal(layout.FieldWidthMm / layout.FieldHeightMm, layout.FieldAspect, 1e-12);
    }
}
