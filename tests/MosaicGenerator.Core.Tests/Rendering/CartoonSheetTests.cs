using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Rendering;
using MosaicGenerator.Core.Tests.Support;

namespace MosaicGenerator.Core.Tests.Rendering;

public class CartoonSheetTests
{
    private static RenderPlan Plan(double panelWidth = 300, double panelHeight = 200) =>
        RenderGeometry.Compute(
            PlanFactory.Striped(seed: 5, panelWidth: panelWidth, panelHeight: panelHeight),
            RenderOptions.Cartoon);

    [Fact]
    public void TheRulerIsAHundredMillimetresLong()
    {
        RenderPlan plan = Plan();

        CartoonSheet sheet = CartoonSheet.Layout(plan);

        Assert.Equal(100.0 * plan.PixelsPerMm, sheet.Ruler.LengthPx, 1.0);
        Assert.Contains(sheet.Ruler.Ticks, t => t.Major
            && Math.Abs(t.X - sheet.Ruler.BarStart.X) < 0.5);
        Assert.Contains(sheet.Ruler.Ticks, t => t.Major
            && Math.Abs(t.X - sheet.Ruler.BarEnd.X) < 0.5);
    }

    [Fact]
    public void TheSheetAddsAStripBelowThePanelAndNothingElse()
    {
        RenderPlan plan = Plan();

        CartoonSheet sheet = CartoonSheet.Layout(plan);

        Assert.Equal(plan.PixelWidth, sheet.WidthPx);
        Assert.True(sheet.HeightPx > plan.PixelHeight);
        Assert.True(sheet.Ruler.BarStart.Y > plan.PixelHeight);
        Assert.True(sheet.HeightPx < plan.PixelHeight * 1.5);
    }
}
