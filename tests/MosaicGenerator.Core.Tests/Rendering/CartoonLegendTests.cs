using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Material;
using MosaicGenerator.Core.Rendering;
using MosaicGenerator.Core.Tests.Support;

namespace MosaicGenerator.Core.Tests.Rendering;

public class CartoonLegendTests
{
    private static MaterialReport Report()
    {
        MosaicPlan plan = PlanFactory.Striped(seed: 5);
        return MaterialCalculator.Calculate(plan, 1.25, 1500m);
    }

    [Fact]
    public void OneRowPerArticleCarryingItsNumberCodeAndCount()
    {
        MaterialReport report = Report();

        CartoonLegend legend = CartoonLegend.Layout(report);

        Assert.Equal(report.Lines.Count, legend.Entries.Count);
        for (int i = 0; i < report.Lines.Count; i++)
        {
            Assert.Equal(report.Lines[i].Code, legend.Entries[i].Code);
            Assert.Equal(report.Lines[i].Color.Article, legend.Entries[i].Article);
            Assert.Equal(report.Lines[i].ModuleCount, legend.Entries[i].ModuleCount);
            Assert.Equal(i, legend.Entries[i].LineIndex);
        }
    }

    [Fact]
    public void EveryEntrySitsInsideTheSheet()
    {
        CartoonLegend legend = CartoonLegend.Layout(Report());

        Assert.All(legend.Entries, e =>
        {
            Assert.True(e.Swatch.X >= 0 && e.Swatch.Right <= legend.WidthPx);
            Assert.True(e.Swatch.Y >= 0 && e.Swatch.Bottom <= legend.HeightPx);
        });
    }
}
