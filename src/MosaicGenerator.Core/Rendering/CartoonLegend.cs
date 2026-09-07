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
    public const double SheetPixelsPerMm = 8.0;
    private const double PadMm = 8.0;
    private const double SwatchMm = 10.0;
    private const double SwatchToTextMm = 4.0;
    private const double TextToEdgeMm = 6.0;
    private const double RowHeightMm = 14.0;
    private const double CapHeightMm = 4.5;
    private const int MaxColumns = 2;

    /// <summary>
    /// The sheet is printed on A4, so it may not grow past the width of one — 210 mm less the
    /// margin no consumer printer reaches into. What does not fit side by side goes below instead.
    /// </summary>
    private const double MaxSheetWidthMm = 194.0;

    /// <summary>
    /// The size the label is drawn at. Public because the caller has to measure its own text at
    /// this size before <see cref="Layout"/> can decide how wide a cell has to be.
    /// </summary>
    public const double LabelFontSizePx = CapHeightMm * SheetPixelsPerMm / 0.7;

    public required int WidthPx { get; init; }

    public required int HeightPx { get; init; }

    public required double PixelsPerMm { get; init; }

    public required double FontSizePx { get; init; }

    /// <summary>One entry per material line, in the report's order (most pieces first).</summary>
    public required IReadOnlyList<CartoonLegendEntry> Entries { get; init; }

    /// <summary>
    /// Lays the sheet out around the labels it will actually carry. <paramref name="measureLabel"/>
    /// gives the width in pixels of a label drawn at <see cref="LabelFontSizePx"/>; the cell is
    /// sized by the longest of them, and the columns by how many such cells fit on an A4 sheet.
    /// Measuring is the whole point: a label carrying a cluster's alternatives (the six whites run
    /// to five of them) is three times as long as a bare one, and a fixed cell width printed it
    /// straight over the neighbouring column — one legend position in twelve came out unreadable.
    /// </summary>
    public static CartoonLegend Layout(MaterialReport report, Func<string, double> measureLabel)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(measureLabel);

        const double ppm = SheetPixelsPerMm;
        double pad = PadMm * ppm;
        double rowH = RowHeightMm * ppm;
        double swatch = SwatchMm * ppm;
        double textOffset = swatch + (SwatchToTextMm * ppm);

        string[] labels = [.. report.Lines.Select(Label)];

        double widest = 0.0;
        foreach (string label in labels)
        {
            widest = Math.Max(widest, measureLabel(label));
        }

        double cellW = textOffset + widest + (TextToEdgeMm * ppm);

        // As many columns as fit beside each other on the sheet, never more than the two the
        // legend was laid out in when every label was short.
        int fitting = (int)Math.Floor(((MaxSheetWidthMm * ppm) - (2.0 * pad)) / cellW);
        int columns = Math.Clamp(fitting, 1, MaxColumns);
        columns = Math.Min(columns, Math.Max(1, report.Lines.Count));

        int rows = (Math.Max(1, report.Lines.Count) + columns - 1) / columns;

        var entries = new List<CartoonLegendEntry>(report.Lines.Count);
        for (int i = 0; i < report.Lines.Count; i++)
        {
            int r = i / columns;
            int c = i % columns;
            double x = pad + (c * cellW);
            double y = pad + (r * rowH);
            entries.Add(new CartoonLegendEntry(
                Swatch: new RectD(x, y + ((rowH - swatch) / 2.0), swatch, swatch),
                TextAnchor: new PointD(x + textOffset, y + (rowH / 2.0)),
                LineIndex: i,
                Code: report.Lines[i].Code,
                Article: report.Lines[i].Color.Article,
                ModuleCount: report.Lines[i].ModuleCount,
                Alternatives: [.. report.Lines[i].Alternatives.Select(a => a.Article)],
                Label: labels[i]));
        }

        return new CartoonLegend
        {
            WidthPx = (int)Math.Round((2.0 * pad) + (columns * cellW)),
            HeightPx = (int)Math.Round((2.0 * pad) + (rows * rowH)),
            PixelsPerMm = ppm,
            FontSizePx = LabelFontSizePx,
            Entries = entries,
        };
    }

    /// <summary>
    /// The one line printed beside a swatch. Built here rather than at the drawing end so the
    /// string that is measured and the string that is drawn cannot drift apart.
    /// </summary>
    private static string Label(MaterialLine line)
    {
        // Brackets, not an arrow: the embedded scheme font has no glyph for one, and an arrow that
        // renders as blank leaves the alternatives looking like a second column of their own.
        string label = $"{line.Code}  {line.Color.Article}  ×{line.ModuleCount}";
        return line.Alternatives.Count > 0
            ? label + $"  (= {string.Join(" ", line.Alternatives.Select(a => a.Article))})"
            : label;
    }
}

public sealed record CartoonLegendEntry(
    RectD Swatch, PointD TextAnchor, int LineIndex, string Code, string Article, int ModuleCount,
    IReadOnlyList<string> Alternatives, string Label);
