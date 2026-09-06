using MosaicGenerator.Core.Material;

namespace MosaicGenerator.Core.Rendering;

/// <summary>
/// The legend, printed on its own sheet so it does not steal room from the mosaic field: one row
/// per article with its swatch, its number, the article code and the piece count. Carried to the
/// bench where the laptop is not. Pure geometry, no graphics.
/// </summary>
public sealed record CartoonLegend
{
    // Its own sheet, so it fixes its own resolution rather than borrowing the cartoon's.
    private const double SheetPixelsPerMm = 8.0;
    private const double PadMm = 8.0;
    private const double SwatchMm = 10.0;
    private const double SwatchToTextMm = 4.0;
    private const double CellWidthMm = 92.0;
    private const double RowHeightMm = 14.0;
    private const double CapHeightMm = 4.5;
    private const int Columns = 2;

    public required int WidthPx { get; init; }

    public required int HeightPx { get; init; }

    public required double PixelsPerMm { get; init; }

    public required double FontSizePx { get; init; }

    /// <summary>One entry per material line, in the report's order (most pieces first).</summary>
    public required IReadOnlyList<CartoonLegendEntry> Entries { get; init; }

    public static CartoonLegend Layout(MaterialReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        const double ppm = SheetPixelsPerMm;
        double pad = PadMm * ppm;
        double cellW = CellWidthMm * ppm;
        double rowH = RowHeightMm * ppm;
        double swatch = SwatchMm * ppm;

        int count = Math.Max(1, report.Lines.Count);
        int columns = Math.Min(Columns, count);
        int rows = (count + columns - 1) / columns;

        var entries = new List<CartoonLegendEntry>(report.Lines.Count);
        for (int i = 0; i < report.Lines.Count; i++)
        {
            int r = i / columns;
            int c = i % columns;
            double x = pad + (c * cellW);
            double y = pad + (r * rowH);
            entries.Add(new CartoonLegendEntry(
                Swatch: new RectD(x, y + ((rowH - swatch) / 2.0), swatch, swatch),
                TextAnchor: new PointD(x + swatch + (SwatchToTextMm * ppm), y + (rowH / 2.0)),
                LineIndex: i,
                Code: report.Lines[i].Code,
                Article: report.Lines[i].Color.Article,
                ModuleCount: report.Lines[i].ModuleCount,
                Alternatives: [.. report.Lines[i].Alternatives.Select(a => a.Article)]));
        }

        return new CartoonLegend
        {
            WidthPx = (int)Math.Round((2.0 * pad) + (columns * cellW)),
            HeightPx = (int)Math.Round((2.0 * pad) + (rows * rowH)),
            PixelsPerMm = ppm,
            FontSizePx = CapHeightMm * ppm / 0.7,
            Entries = entries,
        };
    }
}

public sealed record CartoonLegendEntry(
    RectD Swatch, PointD TextAnchor, int LineIndex, string Code, string Article, int ModuleCount,
    IReadOnlyList<string> Alternatives);
