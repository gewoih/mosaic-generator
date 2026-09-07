namespace MosaicGenerator.Core.Rendering;

public readonly record struct RectI(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;
}

/// <summary>
/// Cuts a finished raster (cartoon or scheme) into A4 sheets for 1:1 printing. Pure geometry, no
/// graphics: <see cref="SkiaPdfTiler"/> paints the pages. The panel's layout in millimetres is
/// fixed; this only decides which pixels land on which sheet and where the sheets overlap.
///
/// Not configurable per run — one master, one way of printing. Constants: A4, an 8 mm printer
/// margin every consumer printer eats, an 18 mm label strip kept inside that so the sheet number
/// and the check ruler always print, and a 20 mm overlap band shared by neighbouring sheets. The
/// band carries the same picture on both sheets, so nothing is lost in the seam however the lap
/// is arranged.
/// </summary>
public sealed record SheetTiling
{
    private const double PaperShortMm = 210.0;
    private const double PaperLongMm = 297.0;
    private const double PrinterMarginMm = 8.0;
    private const double LabelStripMm = 18.0;
    private const double OverlapMm = 20.0;

    public required IReadOnlyList<SheetTile> Tiles { get; init; }

    public required int Columns { get; init; }

    public required int Rows { get; init; }

    /// <summary>True when the A4 page is rotated to 297×210.</summary>
    public required bool Landscape { get; init; }

    public required double PaperWidthMm { get; init; }

    public required double PaperHeightMm { get; init; }

    public required double MarginMm { get; init; }

    /// <summary>Top of the label strip: page-y where the sheet number and check ruler are drawn.</summary>
    public required double LabelStripTopMm { get; init; }

    /// <summary>Width of the band shared by two neighbouring sheets.</summary>
    public required double OverlapMmValue { get; init; }

    public static SheetTiling Plan(int sourceWidthPx, int sourceHeightPx, double pixelsPerMm)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidthPx);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeightPx);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelsPerMm);

        double widthMm = sourceWidthPx / pixelsPerMm;
        double heightMm = sourceHeightPx / pixelsPerMm;

        (int cols, int rows) portrait = GridFor(widthMm, heightMm, PaperShortMm, PaperLongMm);
        (int cols, int rows) landscape = GridFor(widthMm, heightMm, PaperLongMm, PaperShortMm);

        // Tie goes to portrait: it stacks fewer sheets top-to-bottom for the usual near-square panel.
        bool useLandscape = landscape.cols * landscape.rows < portrait.cols * portrait.rows;

        double paperW = useLandscape ? PaperLongMm : PaperShortMm;
        double paperH = useLandscape ? PaperShortMm : PaperLongMm;
        (int columns, int rows) grid = useLandscape ? landscape : portrait;

        double contentW = ContentWidth(paperW);
        double contentH = ContentHeight(paperH);
        double stepX = contentW - OverlapMm;
        double stepY = contentH - OverlapMm;

        var tiles = new List<SheetTile>(grid.columns * grid.rows);
        for (int r = 0; r < grid.rows; r++)
        {
            (double lo, double hi) spanY = Span(heightMm, contentH, stepY, r, grid.rows);
            for (int c = 0; c < grid.columns; c++)
            {
                (double lo, double hi) spanX = Span(widthMm, contentW, stepX, c, grid.columns);

                // The seam line is where the next sheet's content edge actually lands on this one:
                // one step for an ordinary neighbour, more where the last sheet is shifted flush.
                double? seamRight = c < grid.columns - 1
                    ? PrinterMarginMm + (Span(widthMm, contentW, stepX, c + 1, grid.columns).lo - spanX.lo)
                    : null;
                double? seamBottom = r < grid.rows - 1
                    ? PrinterMarginMm + (Span(heightMm, contentH, stepY, r + 1, grid.rows).lo - spanY.lo)
                    : null;

                tiles.Add(new SheetTile
                {
                    Row = r + 1,
                    Col = c + 1,
                    SourceRectPx = ToPixels(spanX, spanY, pixelsPerMm, sourceWidthPx, sourceHeightPx),
                    PlacedMm = new RectD(
                        PrinterMarginMm, PrinterMarginMm, spanX.hi - spanX.lo, spanY.hi - spanY.lo),
                    SeamRightMm = seamRight,
                    SeamBottomMm = seamBottom,
                });
            }
        }

        return new SheetTiling
        {
            Tiles = tiles,
            Columns = grid.columns,
            Rows = grid.rows,
            Landscape = useLandscape,
            PaperWidthMm = paperW,
            PaperHeightMm = paperH,
            MarginMm = PrinterMarginMm,
            LabelStripTopMm = paperH - PrinterMarginMm - LabelStripMm,
            OverlapMmValue = OverlapMm,
        };
    }

    private static double ContentWidth(double paperW) => paperW - (2 * PrinterMarginMm);

    private static double ContentHeight(double paperH) =>
        paperH - (2 * PrinterMarginMm) - LabelStripMm;

    private static (int cols, int rows) GridFor(
        double widthMm, double heightMm, double paperW, double paperH) =>
        (CountAlong(widthMm, ContentWidth(paperW)), CountAlong(heightMm, ContentHeight(paperH)));

    private static int CountAlong(double sizeMm, double contentMm)
    {
        if (sizeMm <= contentMm)
        {
            return 1;
        }

        double step = contentMm - OverlapMm;
        return (int)Math.Ceiling((sizeMm - OverlapMm) / step);
    }

    /// <summary>
    /// The raster range a sheet shows, in millimetres. Every sheet but a lone one carries a full
    /// content width; the last is shifted back to sit flush instead of leaving a sliver.
    /// </summary>
    private static (double lo, double hi) Span(
        double sizeMm, double contentMm, double step, int index, int count)
    {
        if (sizeMm <= contentMm)
        {
            return (0.0, sizeMm);
        }

        double lo = index == count - 1 ? sizeMm - contentMm : index * step;
        return (lo, lo + contentMm);
    }

    private static RectI ToPixels(
        (double lo, double hi) spanX,
        (double lo, double hi) spanY,
        double pixelsPerMm,
        int widthPx,
        int heightPx)
    {
        int x0 = (int)Math.Round(spanX.lo * pixelsPerMm);
        int y0 = (int)Math.Round(spanY.lo * pixelsPerMm);
        int x1 = (int)Math.Round(spanX.hi * pixelsPerMm);
        int y1 = (int)Math.Round(spanY.hi * pixelsPerMm);

        x0 = Math.Clamp(x0, 0, widthPx);
        y0 = Math.Clamp(y0, 0, heightPx);
        x1 = Math.Clamp(x1, x0 + 1, widthPx);
        y1 = Math.Clamp(y1, y0 + 1, heightPx);

        return new RectI(x0, y0, x1 - x0, y1 - y0);
    }
}

/// <summary>
/// One printed A4 page. <see cref="SourceRectPx"/> is the slice of the raster it carries;
/// <see cref="PlacedMm"/> is where that slice sits on the page. <see cref="SeamRightMm"/> and
/// <see cref="SeamBottomMm"/> give the page coordinate of the overlap band's inner edge toward a
/// neighbour — line the neighbour's content edge up with it. Null when there is no neighbour on
/// that side.
/// </summary>
public sealed record SheetTile
{
    public required int Row { get; init; }

    public required int Col { get; init; }

    public required RectI SourceRectPx { get; init; }

    public required RectD PlacedMm { get; init; }

    public required double? SeamRightMm { get; init; }

    public required double? SeamBottomMm { get; init; }
}
