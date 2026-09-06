namespace MosaicGenerator.Core.Rendering;

/// <summary>
/// The cartoon prints 1:1, so it carries a 100 mm scale bar under the panel to catch a printer
/// that has rescaled. Nothing else: the legend is a separate sheet (<see cref="CartoonLegend"/>),
/// and the panel's own edge marks where the panel ends. Pure geometry, no graphics.
/// </summary>
public sealed record CartoonSheet
{
    // Millimetres of the printed sheet, scaled by PixelsPerMm.
    private const double PanelToLabelMm = 5.0;
    private const double LabelToBarMm = 3.0;
    private const double RulerLengthMm = 100.0;
    private const double TickMajorMm = 6.0;
    private const double TickMinorMm = 3.0;
    private const double BottomPadMm = 6.0;
    private const double CapHeightMm = 4.0;

    public required int WidthPx { get; init; }

    public required int HeightPx { get; init; }

    public required CartoonRuler Ruler { get; init; }

    public required double FontSizePx { get; init; }

    public static CartoonSheet Layout(RenderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        double ppm = plan.PixelsPerMm;
        double fontPx = CapHeightMm * ppm / 0.7;

        double labelY = plan.PixelHeight + (PanelToLabelMm * ppm) + fontPx;
        double barY = labelY + (LabelToBarMm * ppm);
        double barStartX = 0.0;
        double barEndX = barStartX + (RulerLengthMm * ppm);

        var ticks = new List<CartoonTick>();
        for (int mm = 0; mm <= (int)RulerLengthMm; mm += 10)
        {
            bool major = mm % 50 == 0;
            ticks.Add(new CartoonTick(
                barStartX + (mm * ppm),
                (major ? TickMajorMm : TickMinorMm) * ppm,
                major));
        }

        var ruler = new CartoonRuler
        {
            BarStart = new PointD(barStartX, barY),
            BarEnd = new PointD(barEndX, barY),
            Ticks = ticks,
            LabelAnchor = new PointD(barStartX, labelY),
            Label = "100 мм",
        };

        double sheetH = barY + (TickMajorMm * ppm) + (BottomPadMm * ppm);

        return new CartoonSheet
        {
            WidthPx = plan.PixelWidth,
            HeightPx = (int)Math.Round(sheetH),
            Ruler = ruler,
            FontSizePx = fontPx,
        };
    }
}

public sealed record CartoonRuler
{
    public required PointD BarStart { get; init; }

    public required PointD BarEnd { get; init; }

    public required IReadOnlyList<CartoonTick> Ticks { get; init; }

    public required PointD LabelAnchor { get; init; }

    public required string Label { get; init; }

    public double LengthPx => BarEnd.X - BarStart.X;
}

public sealed record CartoonTick(double X, double Height, bool Major);
