using MosaicGenerator.Core.Rendering;
using SkiaSharp;

namespace MosaicGenerator.Core.Skia;

/// <summary>
/// Lays a finished raster (cartoon or scheme) across A4 pages of a PDF at 1:1, so a panel wider
/// than a sheet can be printed and glued. The scale is carried in the PDF, not left to the viewer.
///
/// Page 1 is an assembly guide. Every other page carries a slice of the raster, a shaded overlap
/// band toward each neighbour with an alignment line, and a label strip (sheet number, "up" arrow,
/// 50 mm check ruler) kept inside the printable area so a consumer printer cannot clip it.
/// </summary>
public static class SkiaPdfTiler
{
    private const float MmPerInch = 25.4f;
    private const float PointsPerInch = 72f;
    private const double CheckRulerMm = 50.0;

    private static readonly SKColor Ink = new(0x11, 0x11, 0x11);
    private static readonly SKColor BandFill = new(0x2A, 0x60, 0xC0, 0x2E);
    private static readonly SKSamplingOptions Sampling = new(SKCubicResampler.Mitchell);

    /// <param name="sourcePng">Encoded cartoon or scheme PNG, carrying a pHYs scale.</param>
    /// <param name="sheetKindLabel">"Картон" or "Схема" — printed on the guide and the label strip.</param>
    public static byte[] Render(byte[] sourcePng, string sheetKindLabel)
    {
        ArgumentNullException.ThrowIfNull(sourcePng);
        ArgumentException.ThrowIfNullOrEmpty(sheetKindLabel);

        double? scale = PngMetadata.ReadPhysicalScale(sourcePng);
        if (scale is not { } pixelsPerMm)
        {
            throw new ArgumentException(
                "The PNG has no pHYs scale; printing it 1:1 would guess the size.", nameof(sourcePng));
        }

        using SKImage image = SKImage.FromEncodedData(sourcePng)
            ?? throw new ArgumentException("Not a decodable image.", nameof(sourcePng));

        SheetTiling tiling = SheetTiling.Plan(image.Width, image.Height, pixelsPerMm);

        using var buffer = new MemoryStream();
        using (SKDocument document = SKDocument.CreatePdf(buffer)
            ?? throw new InvalidOperationException("PDF backend unavailable."))
        {
            float pageW = ToPoints(tiling.PaperWidthMm);
            float pageH = ToPoints(tiling.PaperHeightMm);

            if (tiling.Tiles.Count > 1)
            {
                SKCanvas guide = document.BeginPage(pageW, pageH);
                guide.Clear(SKColors.White);
                DrawGuide(guide, tiling, sheetKindLabel);
                document.EndPage();
            }

            foreach (SheetTile tile in tiling.Tiles)
            {
                SKCanvas canvas = document.BeginPage(pageW, pageH);
                canvas.Clear(SKColors.White);
                DrawTile(canvas, image, tile, tiling, pixelsPerMm, sheetKindLabel);
                document.EndPage();
            }

            document.Close();
        }

        return buffer.ToArray();
    }

    private static void DrawTile(
        SKCanvas canvas,
        SKImage image,
        SheetTile tile,
        SheetTiling tiling,
        double pixelsPerMm,
        string sheetKindLabel)
    {
        RectI src = tile.SourceRectPx;
        var source = SKRect.Create(src.X, src.Y, src.Width, src.Height);
        var dest = SKRect.Create(
            ToPoints(tile.PlacedMm.X), ToPoints(tile.PlacedMm.Y),
            ToPoints(src.Width / pixelsPerMm), ToPoints(src.Height / pixelsPerMm));

        using (var imagePaint = new SKPaint { IsAntialias = true })
        {
            canvas.DrawImage(image, source, dest, Sampling, imagePaint);
        }

        using var stroke = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = Ink,
            StrokeWidth = ToPoints(0.35),
        };
        using var band = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = BandFill };
        using var text = new SKPaint { IsAntialias = true, Color = Ink };
        using var font = new SKFont(SchemeFont.Typeface, ToPoints(3.0));

        DrawSeams(canvas, tile, stroke, band, text, font);
        DrawLabelStrip(canvas, tile, tiling, sheetKindLabel, stroke, text, font);
    }

    private static void DrawSeams(
        SKCanvas canvas, SheetTile tile, SKPaint stroke, SKPaint band, SKPaint text, SKFont font)
    {
        RectD placed = tile.PlacedMm;

        if (tile.SeamRightMm is { } seamX)
        {
            var rect = new SKRect(
                ToPoints(seamX), ToPoints(placed.Y), ToPoints(placed.Right), ToPoints(placed.Bottom));
            canvas.DrawRect(rect, band);
            canvas.DrawLine(rect.Left, rect.Top, rect.Left, rect.Bottom, stroke);
            Ticks(canvas, stroke, rect.Left, rect.Top, rect.Bottom, horizontal: false);
            canvas.Save();
            canvas.RotateDegrees(90, rect.Left, rect.Top);
            canvas.DrawText(
                "нахлёст — сюда левый край соседнего листа",
                rect.Left + ToPoints(4), rect.Top - ToPoints(1.5), SKTextAlign.Left, font, text);
            canvas.Restore();
        }

        if (tile.SeamBottomMm is { } seamY)
        {
            var rect = new SKRect(
                ToPoints(placed.X), ToPoints(seamY), ToPoints(placed.Right), ToPoints(placed.Bottom));
            canvas.DrawRect(rect, band);
            canvas.DrawLine(rect.Left, rect.Top, rect.Right, rect.Top, stroke);
            Ticks(canvas, stroke, rect.Top, rect.Left, rect.Right, horizontal: true);
            canvas.DrawText(
                "нахлёст — сюда верхний край соседнего листа",
                rect.Left + ToPoints(4), rect.Top - ToPoints(1.5), SKTextAlign.Left, font, text);
        }
    }

    private static void Ticks(
        SKCanvas canvas, SKPaint stroke, float line, float from, float to, bool horizontal)
    {
        float len = ToPoints(3.0);
        foreach (float f in new[] { 0.25f, 0.75f })
        {
            float at = from + ((to - from) * f);
            if (horizontal)
            {
                canvas.DrawLine(at, line - len, at, line + len, stroke);
            }
            else
            {
                canvas.DrawLine(line - len, at, line + len, at, stroke);
            }
        }
    }

    private static void DrawLabelStrip(
        SKCanvas canvas,
        SheetTile tile,
        SheetTiling tiling,
        string sheetKindLabel,
        SKPaint stroke,
        SKPaint text,
        SKFont font)
    {
        float baseX = ToPoints(tiling.MarginMm);
        float lineY = ToPoints(tiling.LabelStripTopMm);
        float textY = lineY + ToPoints(6.0);

        canvas.DrawLine(baseX, lineY, ToPoints(tiling.PaperWidthMm - tiling.MarginMm), lineY, stroke);

        string caption =
            $"{sheetKindLabel}:  ряд {tile.Row} из {tiling.Rows}   ·   столбец {tile.Col} из {tiling.Columns}   ·   верх ";
        canvas.DrawText(caption, baseX, textY, SKTextAlign.Left, font, text);

        using var probe = new SKPaint();
        float arrowX = baseX + font.MeasureText(caption, probe) + ToPoints(1.5);
        UpArrow(canvas, stroke, arrowX, textY, ToPoints(3.2));

        // 50 mm check ruler: measure it on the print to catch a rescaled printer.
        float rulerY = textY + ToPoints(7.0);
        float rulerEnd = baseX + ToPoints(CheckRulerMm);
        canvas.DrawLine(baseX, rulerY, rulerEnd, rulerY, stroke);
        for (int mm = 0; mm <= (int)CheckRulerMm; mm += 10)
        {
            float x = baseX + ToPoints(mm);
            canvas.DrawLine(x, rulerY, x, rulerY - ToPoints(mm % 50 == 0 ? 2.4 : 1.4), stroke);
        }

        canvas.DrawText("50 мм — проверьте линейкой", rulerEnd + ToPoints(3.0), rulerY, SKTextAlign.Left, font, text);
    }

    private static void DrawGuide(SKCanvas canvas, SheetTiling tiling, string sheetKindLabel)
    {
        float margin = ToPoints(tiling.MarginMm + 6);
        using var stroke = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke, Color = Ink, StrokeWidth = ToPoints(0.35),
        };
        using var fill = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Fill, Color = BandFill,
        };
        using var text = new SKPaint { IsAntialias = true, Color = Ink };
        using var title = new SKFont(SchemeFont.Typeface, ToPoints(5.0));
        using var body = new SKFont(SchemeFont.Typeface, ToPoints(3.4));
        using var small = new SKFont(SchemeFont.Typeface, ToPoints(2.8));

        float y = margin + ToPoints(6);
        canvas.DrawText(
            $"{sheetKindLabel}: сборка из {tiling.Tiles.Count} листов A4 ({tiling.Rows} × {tiling.Columns})",
            margin, y, SKTextAlign.Left, title, text);

        // Grid map: cell (1,1) is the top-left of the panel.
        float cell = ToPoints(16);
        float gridTop = y + ToPoints(8);
        for (int r = 0; r < tiling.Rows; r++)
        {
            for (int c = 0; c < tiling.Columns; c++)
            {
                var box = SKRect.Create(margin + (c * cell), gridTop + (r * cell), cell, cell);
                if (r == 0 && c == 0)
                {
                    canvas.DrawRect(box, fill);
                }

                canvas.DrawRect(box, stroke);
                canvas.DrawText(
                    $"р{r + 1}·с{c + 1}",
                    box.MidX, box.MidY + ToPoints(1.0), SKTextAlign.Center, small, text);
            }
        }

        UpArrow(canvas, stroke, margin - ToPoints(3), gridTop + cell, cell * 0.8f);
        canvas.DrawText(
            "верх", margin - ToPoints(6), gridTop + (cell * 0.5f), SKTextAlign.Right, small, text);

        float sy = gridTop + (tiling.Rows * cell) + ToPoints(10);
        string[] steps =
        [
            "1. Разложите листы сеткой: ряд 1 сверху, столбец 1 слева (см. схему).",
            "2. Полоса нахлёста напечатана на ОБОИХ соседних листах — одна и та же",
            "   часть картинки. В шве ничего не теряется, класть внахлёст можно как удобно.",
            "3. Возьмите лист р1·с1. Наложите р1·с2 так, чтобы рисунок в полосе нахлёста",
            "   точно совпал (проще на просвет у окна). Совместите край картинки соседа",
            "   с линией на полосе. Проклейте по полосе.",
            "4. Так весь ряд вправо, затем ряд 2 подведите снизу к ряду 1.",
            "5. На каждом листе линейка 50 мм — проверьте линейкой. Если не 50 мм,",
            "   принтер сжал печать: печатайте «100 %», «Реальный размер», без «вписать».",
        ];
        foreach (string step in steps)
        {
            canvas.DrawText(step, margin, sy, SKTextAlign.Left, body, text);
            sy += ToPoints(6.5);
        }
    }

    private static void UpArrow(SKCanvas canvas, SKPaint stroke, float x, float baseY, float height)
    {
        float head = height * 0.4f;
        canvas.DrawLine(x, baseY, x, baseY - height, stroke);
        canvas.DrawLine(x, baseY - height, x - (head * 0.6f), baseY - height + head, stroke);
        canvas.DrawLine(x, baseY - height, x + (head * 0.6f), baseY - height + head, stroke);
    }

    private static float ToPoints(double millimetres) =>
        (float)(millimetres / MmPerInch * PointsPerInch);
}
