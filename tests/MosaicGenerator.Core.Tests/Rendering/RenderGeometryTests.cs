using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Rendering;
using MosaicGenerator.Core.Tests.Support;

namespace MosaicGenerator.Core.Tests.Rendering;

public class RenderGeometryTests
{
    [Fact]
    public void TheSameSeedReproducesTheGeometryExactly()
    {
        MosaicPlan first = PlanFactory.Striped(seed: 12345);
        MosaicPlan second = PlanFactory.Striped(seed: 12345);

        RenderPlan a = RenderGeometry.Compute(first, RenderOptions.Preview);
        RenderPlan b = RenderGeometry.Compute(second, RenderOptions.Preview);

        Assert.Equal(Digest(a), Digest(b));
    }

    [Fact]
    public void ADifferentSeedProducesADifferentSurfaceButTheSameFootprints()
    {
        RenderPlan a = RenderGeometry.Compute(
            PlanFactory.Striped(seed: 12345), RenderOptions.Preview);
        RenderPlan b = RenderGeometry.Compute(
            PlanFactory.Striped(seed: 999), RenderOptions.Preview);

        Assert.NotEqual(Digest(a), Digest(b));

        // Only the shaping and tone move; the module footprints — layout plus the brick offset —
        // are the same either way.
        Assert.Equal(a.PixelWidth, b.PixelWidth);
        Assert.Equal(
            a.Modules.Select(m => m.Bounds),
            b.Modules.Select(m => m.Bounds));
    }

    [Fact]
    public void GeometryDependsOnlyOnTheCellSoIterationOrderCannotLeakIn()
    {
        RenderPlan plan = RenderGeometry.Compute(
            PlanFactory.Striped(seed: 7), RenderOptions.Preview);

        RenderedModule module = plan.Modules.Single(m => m is { Row: 3, Column: 5 });

        // The first draw off the cell's own stream sizes the tessera; reproduce it independently.
        var random = new DeterministicRandom(7, 3, 5);
        double expectedHalfWidth =
            module.Bounds.Width / 2.0 * (1.0 + (random.NextSigned() * RenderOptions.Preview.SizeJitter));

        double widestFromCentre = module.Quad
            .Max(p => Math.Abs(p.X - (module.Bounds.X + (module.Bounds.Width / 2.0))));

        // Rotation only ever adds to the reach from the centre, never subtracts.
        Assert.True(widestFromCentre >= expectedHalfWidth - (module.Bounds.Width * RenderOptions.Preview.EdgeRoughness));
    }

    [Fact]
    public void TheNominalGridDoesNotStaggerItsCourses()
    {
        // The half-step stagger belongs to a followed course, not the plain fallback grid.
        RenderPlan plan = RenderGeometry.Compute(
            PlanFactory.Striped(seed: 1), RenderOptions.Preview);

        RenderedModule even = plan.Modules.Single(m => m is { Row: 2, Column: 3 });
        RenderedModule odd = plan.Modules.Single(m => m is { Row: 3, Column: 3 });

        Assert.Equal(even.Bounds.X, odd.Bounds.X, 1e-6);
    }

    [Fact]
    public void ShapedTesseraeStayInsideTheField()
    {
        RenderPlan plan = RenderGeometry.Compute(
            PlanFactory.Striped(seed: 42), RenderOptions.Preview);
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

    [Fact]
    public void EdgeRoughnessAndRotationStayWithinTheirEnvelope()
    {
        RenderPlan plan = RenderGeometry.Compute(
            PlanFactory.Striped(seed: 42), RenderOptions.Preview);
        RenderOptions o = RenderOptions.Preview;

        foreach (RenderedModule module in plan.Modules)
        {
            double cx = module.Bounds.X + (module.Bounds.Width / 2.0);
            double cy = module.Bounds.Y + (module.Bounds.Height / 2.0);

            double half = Math.Sqrt((module.Bounds.Width * module.Bounds.Width)
                + (module.Bounds.Height * module.Bounds.Height)) / 2.0;
            double slack = (half * (1.0 + o.SizeJitter))
                + (module.Bounds.Width * o.EdgeRoughness)
                + (module.Bounds.Height * o.EdgeRoughness)
                + (half * Math.Sin(o.RotationJitterDeg * Math.PI / 180.0))
                + 1e-6;

            foreach (PointD point in module.Quad)
            {
                // Field clamping can only pull a corner closer to the centre, never push it out.
                double dx = point.X - cx;
                double dy = point.Y - cy;
                Assert.True(Math.Sqrt((dx * dx) + (dy * dy)) <= slack);
            }
        }
    }

    [Fact]
    public void TheSchemeUsesCleanRectanglesAndTheUntouchedPaletteColour()
    {
        MosaicPlan plan = PlanFactory.Striped(seed: 42);
        RenderPlan rendered = RenderGeometry.Compute(plan, RenderOptions.Scheme);

        foreach (RenderedModule module in rendered.Modules)
        {
            Assert.Equal(module.Bounds.X, module.Quad[0].X, 1e-9);
            Assert.Equal(module.Bounds.Y, module.Quad[0].Y, 1e-9);
            Assert.Equal(module.Bounds.Right, module.Quad[2].X, 1e-9);
            Assert.Equal(module.Bounds.Bottom, module.Quad[2].Y, 1e-9);
            Assert.Equal(
                plan.Palette.Colors[module.ColorIndex].Rgb.ToBytes(),
                module.FillColor.ToBytes());

            // No gloss ramp on the scheme: both edges are the face colour.
            Assert.Equal(module.FillColor.ToBytes(), module.GlossLow.ToBytes());
            Assert.Equal(module.FillColor.ToBytes(), module.GlossHigh.ToBytes());
        }
    }

    [Fact]
    public void ToneJitterStaysWithinTheConfiguredLightnessSpread()
    {
        MosaicPlan plan = PlanFactory.Striped(seed: 42, hexes: ["#808080", "#6E6E6E"]);
        var options = RenderOptions.Preview with { ToneJitter = 4.0 };

        RenderPlan rendered = RenderGeometry.Compute(plan, options);

        var spread = new List<double>();
        foreach (RenderedModule module in rendered.Modules)
        {
            double expected = plan.Palette.Colors[module.ColorIndex].Lab.L;
            double actual = module.FillColor.ToLab().L;
            Assert.InRange(actual, expected - 4.0 - 1e-6, expected + 4.0 + 1e-6);
            spread.Add(actual - expected);
        }

        // The spread has to be real, not a rounding artefact.
        Assert.True(spread.Max() - spread.Min() > 4.0, "Tone jitter barely moved the modules.");
    }

    [Fact]
    public void TheGlossRampBracketsTheFaceColour()
    {
        MosaicPlan plan = PlanFactory.Striped(seed: 42, hexes: ["#808080"]);
        var options = RenderOptions.Preview with { ToneJitter = 0.0, GlossJitter = 3.5 };

        RenderPlan rendered = RenderGeometry.Compute(plan, options);

        foreach (RenderedModule module in rendered.Modules)
        {
            double low = module.GlossLow.ToLab().L;
            double mid = module.FillColor.ToLab().L;
            double high = module.GlossHigh.ToLab().L;

            Assert.True(low < mid && mid < high, $"expected {low:0.0} < {mid:0.0} < {high:0.0}");
            Assert.Equal(3.5, mid - low, 0.2);
            Assert.Equal(3.5, high - mid, 0.2);
        }
    }

    [Fact]
    public void ModulePositionsFollowTheLayoutInMillimetres()
    {
        MosaicPlan plan = PlanFactory.Striped(seed: 1);
        RenderPlan rendered = RenderGeometry.Compute(plan, RenderOptions.Preview);
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
        var options = RenderOptions.Preview with { PixelsPerStep = 24 };
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
                .Append(module.FillColor.ToHex()).Append(',')
                .Append(module.GlossLow.ToHex()).Append(',')
                .Append(module.GlossHigh.ToHex()).Append(',');
            foreach (PointD point in module.Quad)
            {
                builder.Append(point.X.ToString("R")).Append(':').Append(point.Y.ToString("R")).Append(' ');
            }

            builder.Append(';');
        }

        return builder.ToString();
    }
}
