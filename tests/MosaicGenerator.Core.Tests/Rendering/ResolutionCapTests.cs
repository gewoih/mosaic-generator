using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Rendering;
using MosaicGenerator.Core.Tests.Support;

namespace MosaicGenerator.Core.Tests.Rendering;

public class ResolutionCapTests
{
    [Fact]
    public void AnOversizedPanelIsPulledUnderTheLongSideCap()
    {
        // 3000 x 400 mm at 48 px per 23 mm step would be 6260 px wide.
        MosaicPlan plan = PlanFactory.Striped(panelWidth: 3000, panelHeight: 400, seed: 1);
        var options = RenderOptions.Scheme with { MaxLongSidePx = 6000, MaxTotalPixels = long.MaxValue };

        RenderPlan rendered = RenderGeometry.Compute(plan, options);

        Assert.True(rendered.PixelWidth <= 6000, $"width was {rendered.PixelWidth}");
        Assert.True(rendered.Layout.StepXMm * rendered.PixelsPerMm < 48.0);
    }

    [Fact]
    public void AnOversizedPanelIsPulledUnderTheTotalPixelCap()
    {
        MosaicPlan plan = PlanFactory.Striped(panelWidth: 3000, panelHeight: 3000, seed: 1);
        var options = RenderOptions.Scheme with { MaxLongSidePx = int.MaxValue, MaxTotalPixels = 30_000_000 };

        RenderPlan rendered = RenderGeometry.Compute(plan, options);

        Assert.True(
            (long)rendered.PixelWidth * rendered.PixelHeight <= 30_000_000,
            $"raster was {rendered.PixelWidth}x{rendered.PixelHeight}");
    }

    [Fact]
    public void BothCapsHoldTogetherOnTheLargestAllowedPanel()
    {
        MosaicPlan plan = PlanFactory.Striped(panelWidth: 3000, panelHeight: 3000, module: 5, grout: 1, seed: 1);

        RenderPlan rendered = RenderGeometry.Compute(plan, RenderOptions.Scheme);

        Assert.True(Math.Max(rendered.PixelWidth, rendered.PixelHeight) <= RenderOptions.Scheme.MaxLongSidePx);
        Assert.True((long)rendered.PixelWidth * rendered.PixelHeight <= RenderOptions.Scheme.MaxTotalPixels);
    }

    [Fact]
    public void ThePixelScaleStaysConsistentWithTheRasterSize()
    {
        MosaicPlan plan = PlanFactory.Striped(panelWidth: 3000, panelHeight: 1800, seed: 1);

        RenderPlan rendered = RenderGeometry.Compute(plan, RenderOptions.Scheme);

        Assert.Equal(rendered.PixelWidth, plan.Layout.PanelWidthMm * rendered.PixelsPerMm, 0.5);
        Assert.Equal(rendered.PixelHeight, plan.Layout.PanelHeightMm * rendered.PixelsPerMm, 0.5);

        // Modules cannot spill outside the raster they were scaled to fit.
        Assert.True(rendered.Modules.Max(m => m.Bounds.Right) <= rendered.PixelWidth + 1);
        Assert.True(rendered.Modules.Max(m => m.Bounds.Bottom) <= rendered.PixelHeight + 1);
    }

    [Fact]
    public void TheRasterCoversExactlyThePhysicalPanel()
    {
        RenderPlan rendered = RenderGeometry.Compute(
            PlanFactory.Striped(panelWidth: 1200, panelHeight: 800, seed: 1),
            RenderOptions.Scheme);

        Assert.Equal(rendered.PixelsPerMm * 25.4, rendered.Dpi, 1e-9);

        // Printed at the stamped density, the sheet is the panel's real size.
        Assert.Equal(1200.0, rendered.PixelWidth / rendered.PixelsPerMm, 0.5);
        Assert.Equal(800.0, rendered.PixelHeight / rendered.PixelsPerMm, 0.5);
    }
}
