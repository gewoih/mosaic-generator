using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Grid;
using MosaicGenerator.Core.Rendering;
using SkiaSharp;

namespace MosaicGenerator.Diag;

/// <summary>
/// The wide gaps, drawn where they are. <see cref="CoverageMask.JointWidths"/> says how much of the
/// panel sits under a gap and how wide the worst one is, and the autopsy says which role of piece is
/// nearest to them — neither answers the question the backlog actually asks about TODO п. 4, which is
/// what the holes look like on the work: strung along a course, gathered on the outside of a bend,
/// scattered, or piled where the border meets the fill.
///
/// Pieces in outline, every raster point sitting in a gap wider than the threshold painted over them.
/// Not a cartoon and not for the user — a picture to read the defect off.
/// </summary>
public static class HoleMap
{
    private const double PxPerMm = 8.0;

    public static void Write(
        string path, MosaicLayout layout, IReadOnlyList<Tessera> tesserae, double wideMm)
    {
        int w = (int)Math.Ceiling(layout.FieldWidthMm * PxPerMm);
        int h = (int)Math.Ceiling(layout.FieldHeightMm * PxPerMm);

        using var surface = SKSurface.Create(new SKImageInfo(w, h));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(0x20, 0x20, 0x22));

        using var fill = new SKPaint { Color = new SKColor(0xC8, 0xC8, 0xCC), IsAntialias = true };
        foreach (Tessera t in tesserae)
        {
            using var p = new SKPath();
            p.MoveTo((float)(t.Polygon[0].X * PxPerMm), (float)(t.Polygon[0].Y * PxPerMm));
            for (int i = 1; i < t.Polygon.Length; i++)
            {
                p.LineTo((float)(t.Polygon[i].X * PxPerMm), (float)(t.Polygon[i].Y * PxPerMm));
            }

            p.Close();
            canvas.DrawPath(p, fill);
        }

        // One dot per raster point of the clearance grid, sized to that grid, so a run of them reads
        // as the shape of the gap rather than as scattered specks.
        double cell = layout.ModuleWidthMm / 40.0;
        float dot = (float)(cell * PxPerMm);
        using var hole = new SKPaint { Color = new SKColor(0x40, 0x70, 0xC0), IsAntialias = false };
        foreach (PointD p in CoverageMask.WideGapPoints(layout, tesserae, wideMm))
        {
            canvas.DrawRect(
                (float)((p.X - (cell / 2.0)) * PxPerMm), (float)((p.Y - (cell / 2.0)) * PxPerMm),
                dot, dot, hole);
        }

        // Blue is adhesive the material forbids filling — a wedge narrower than the 5 mm hand limit,
        // which a mosaicist would leave too. Ringed in magenta is the rare wedge that could have
        // taken a piece: that, and only that, is what TODO п. 4 is worth chasing. Drawing the two
        // apart is the whole point of the map — the eye cannot tell them apart on the cartoon, and
        // the jointArea column counts them together.
        (_, _, _, _, IReadOnlyList<CoverageMask.Wedge> wedges) =
            CoverageMask.Wedges(layout, tesserae, layout.GroutWidthMm * 1.5, 5.0);
        using var mark = new SKPaint
        {
            Color = new SKColor(0xFF, 0x00, 0x99),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3f,
        };
        foreach (CoverageMask.Wedge wedge in wedges.Where(x => x.Fillable))
        {
            canvas.DrawCircle(
                (float)(wedge.Where.X * PxPerMm), (float)(wedge.Where.Y * PxPerMm),
                (float)(3.0 * PxPerMm), mark);
        }

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream file = File.Create(path);
        data.SaveTo(file);
    }
}
