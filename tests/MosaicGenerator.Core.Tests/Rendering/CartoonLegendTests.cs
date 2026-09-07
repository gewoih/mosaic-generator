using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Material;
using MosaicGenerator.Core.Rendering;
using MosaicGenerator.Core.Tests.Support;

namespace MosaicGenerator.Core.Tests.Rendering;

public class CartoonLegendTests
{
    // A stand-in for a monospaced face: every glyph the same width, so a label's width is its
    // length. The real sheet measures its own font; the invariants under test are the same.
    private const double GlyphWidthPx = 0.6 * CartoonLegend.LabelFontSizePx;

    private static double Measure(string label) => label.Length * GlyphWidthPx;

    private static MaterialReport Report()
    {
        MosaicPlan plan = PlanFactory.Striped(seed: 5);
        return MaterialCalculator.Calculate(plan, 1.25, 1500m);
    }

    [Fact]
    public void OneRowPerArticleCarryingItsNumberCodeAndCount()
    {
        MaterialReport report = Report();

        CartoonLegend legend = CartoonLegend.Layout(report, Measure);

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
        CartoonLegend legend = CartoonLegend.Layout(Report(), Measure);

        Assert.All(legend.Entries, e =>
        {
            Assert.True(e.Swatch.X >= 0 && e.Swatch.Right <= legend.WidthPx);
            Assert.True(e.Swatch.Y >= 0 && e.Swatch.Bottom <= legend.HeightPx);
        });
    }

    /// <summary>
    /// The defect this layout was rewritten for: the label ran past its own cell and printed over
    /// the neighbouring column, wiping out a legend position. Measured, not eyeballed.
    /// </summary>
    [Fact]
    public void NoLabelRunsPastItsOwnCell()
    {
        MaterialReport report = Report();

        CartoonLegend legend = CartoonLegend.Layout(report, Measure);

        // Cells in one row are a fixed pitch apart, so the pitch is the width a label may occupy
        // from its anchor. On a single-column sheet the sheet edge is the bound instead.
        var byRow = legend.Entries
            .GroupBy(e => Math.Round(e.TextAnchor.Y, 3))
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.TextAnchor.X).ToList());

        foreach (CartoonLegendEntry entry in legend.Entries)
        {
            List<CartoonLegendEntry> row = byRow[Math.Round(entry.TextAnchor.Y, 3)];
            int at = row.IndexOf(entry);
            double bound = at + 1 < row.Count ? row[at + 1].Swatch.X : legend.WidthPx;

            Assert.True(
                entry.TextAnchor.X + Measure(entry.Label) <= bound + 1e-6,
                $"Label '{entry.Label}' runs past {bound:F1} px.");
        }
    }

    /// <summary>
    /// A cluster's alternatives triple the length of a label. The sheet pays for that in columns —
    /// it narrows to one and stays on A4 — rather than by printing one label over the next.
    /// </summary>
    [Fact]
    public void ALongLabelCostsAColumnRatherThanReadability()
    {
        MaterialReport report = Report();

        CartoonLegend narrow = CartoonLegend.Layout(report, Measure);
        CartoonLegend wide = CartoonLegend.Layout(report, label => Measure(label) * 3.0);

        Assert.Contains(narrow.Entries.GroupBy(e => Math.Round(e.TextAnchor.Y, 3)), g => g.Count() > 1);
        Assert.All(wide.Entries.GroupBy(e => Math.Round(e.TextAnchor.Y, 3)), g => Assert.Single(g));
        Assert.True(
            wide.WidthPx <= 210.0 * CartoonLegend.SheetPixelsPerMm,
            $"Sheet is {wide.WidthPx / CartoonLegend.SheetPixelsPerMm:F0} mm wide, past an A4.");
    }
}
