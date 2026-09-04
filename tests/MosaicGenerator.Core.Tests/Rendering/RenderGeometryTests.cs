using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Rendering;
using MosaicGenerator.Core.Tests.Support;

namespace MosaicGenerator.Core.Tests.Rendering;

public class RenderGeometryTests
{
    [Fact]
    public void TheSamePlanReproducesTheGeometryExactly()
    {
        RenderPlan a = RenderGeometry.Compute(PlanFactory.Striped(seed: 12345), RenderOptions.Cartoon);
        RenderPlan b = RenderGeometry.Compute(PlanFactory.Striped(seed: 12345), RenderOptions.Cartoon);

        Assert.Equal(Digest(a), Digest(b));
    }

    [Fact]
    public void TheNominalGridDoesNotStaggerItsCourses()
    {
        // The half-step stagger belongs to a followed course, not the plain fallback grid.
        RenderPlan plan = RenderGeometry.Compute(
            PlanFactory.Striped(seed: 1), RenderOptions.Cartoon);

        RenderedModule even = plan.Modules.Single(m => m is { Row: 2, Column: 3 });
        RenderedModule odd = plan.Modules.Single(m => m is { Row: 3, Column: 3 });

        Assert.Equal(even.Bounds.X, odd.Bounds.X, 1e-6);
    }

    [Fact]
    public void TesseraeStayInsideTheField()
    {
        RenderPlan plan = RenderGeometry.Compute(
            PlanFactory.Striped(seed: 42), RenderOptions.Cartoon);
        MosaicLayout layout = plan.Layout;

        double left = (layout.MarginXMm * plan.PixelsPerMm) - 1e-6;
        double right = ((layout.MarginXMm + layout.FieldWidthMm) * plan.PixelsPerMm) + 1e-6;
        double top = (layout.MarginYMm * plan.PixelsPerMm) - 1e-6;
        double bottom = ((layout.MarginYMm + layout.FieldHeightMm) * plan.PixelsPerMm) + 1e-6;

        foreach (RenderedModule module in plan.Modules)
        {
            Assert.Equal(4, module.Quad.Length);
            foreach (PointD point in module.Quad)
            {
                Assert.InRange(point.X, left, right);
                Assert.InRange(point.Y, top, bottom);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryModuleIsFilledFlatWithItsUntouchedPaletteColour(bool scheme)
    {
        MosaicPlan plan = PlanFactory.Striped(seed: 42);
        RenderOptions options = scheme ? RenderOptions.Scheme : RenderOptions.Cartoon;

        RenderPlan rendered = RenderGeometry.Compute(plan, options);

        var byColour = new Dictionary<int, (byte R, byte G, byte B)>();
        foreach (RenderedModule module in rendered.Modules)
        {
            (byte R, byte G, byte B) expected = plan.Palette.Colors[module.ColorIndex].Rgb.ToBytes();
            Assert.Equal(expected, module.FillColor.ToBytes());

            // Two pieces of one article read as one colour — the whole point of the cartoon.
            if (byColour.TryGetValue(module.ColorIndex, out (byte R, byte G, byte B) seen))
            {
                Assert.Equal(seen, module.FillColor.ToBytes());
            }
            else
            {
                byColour[module.ColorIndex] = module.FillColor.ToBytes();
            }
        }
    }

    [Fact]
    public void TheSchemeUsesCleanRectangles()
    {
        RenderPlan rendered = RenderGeometry.Compute(PlanFactory.Striped(seed: 42), RenderOptions.Scheme);

        foreach (RenderedModule module in rendered.Modules)
        {
            Assert.Equal(module.Bounds.X, module.Quad[0].X, 1e-9);
            Assert.Equal(module.Bounds.Y, module.Quad[0].Y, 1e-9);
            Assert.Equal(module.Bounds.Right, module.Quad[2].X, 1e-9);
            Assert.Equal(module.Bounds.Bottom, module.Quad[2].Y, 1e-9);
        }
    }

    [Fact]
    public void ModulePositionsFollowTheLayoutInMillimetres()
    {
        MosaicPlan plan = PlanFactory.Striped(seed: 1);
        RenderPlan rendered = RenderGeometry.Compute(plan, RenderOptions.Cartoon);
        MosaicLayout layout = plan.Layout;

        Assert.Equal(layout.TotalModules, rendered.Modules.Count);

        RenderedModule module = rendered.Modules.Single(m => m is { Row: 2, Column: 4 });
        Assert.Equal(layout.ModuleLeftMm(4) * rendered.PixelsPerMm, module.Bounds.X, 1e-9);
        Assert.Equal(layout.ModuleTopMm(2) * rendered.PixelsPerMm, module.Bounds.Y, 1e-9);
        Assert.Equal(layout.ModuleWidthMm * rendered.PixelsPerMm, module.Bounds.Width, 1e-9);
    }

    [Fact]
    public void RequestedPixelsPerStepIsHonouredWhenNoCapBinds()
    {
        var options = RenderOptions.Cartoon with { PixelsPerStep = 24 };
        RenderPlan rendered = RenderGeometry.Compute(PlanFactory.Striped(seed: 1), options);

        Assert.Equal(24.0, rendered.Layout.StepXMm * rendered.PixelsPerMm, 1e-9);
    }

    private static string Digest(RenderPlan plan)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append(plan.PixelWidth).Append('x').Append(plan.PixelHeight).Append(';');

        foreach (RenderedModule module in plan.Modules)
        {
            builder.Append(module.Row).Append(',').Append(module.Column).Append(',')
                .Append(module.ColorIndex).Append(',')
                .Append(module.FillColor.ToHex()).Append(',');
            foreach (PointD point in module.Quad)
            {
                builder.Append(point.X.ToString("R")).Append(':').Append(point.Y.ToString("R")).Append(' ');
            }

            builder.Append(';');
        }

        return builder.ToString();
    }
}
