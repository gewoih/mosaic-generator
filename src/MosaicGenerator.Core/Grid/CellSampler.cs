using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Imaging;
using MosaicGenerator.Core.Rendering;

namespace MosaicGenerator.Core.Grid;

public static class CellSampler
{
    /// <summary>
    /// One average colour per module, row-major. Each module samples only its own footprint —
    /// the grout gaps between modules are skipped, so joints do not bleed into the colours.
    /// </summary>
    public static LinearRgb[] Sample(SourceImage image, CropRect crop, MosaicLayout layout)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(layout);

        var cells = new LinearRgb[layout.TotalModules];

        double fieldWidth = layout.FieldWidthMm;
        double fieldHeight = layout.FieldHeightMm;

        for (int row = 0; row < layout.Rows; row++)
        {
            double top = row * layout.StepYMm;
            (int y0, int y1) = MapSpan(top, top + layout.ModuleHeightMm, fieldHeight, crop.Y, crop.Height);

            for (int column = 0; column < layout.Columns; column++)
            {
                double left = column * layout.StepXMm;
                (int x0, int x1) = MapSpan(left, left + layout.ModuleWidthMm, fieldWidth, crop.X, crop.Width);

                cells[(row * layout.Columns) + column] = image.AverageLinear(x0, y0, x1 - x0, y1 - y0);
            }
        }

        return cells;
    }

    /// <summary>
    /// One average colour per tessera, sampled under its actual outline in field millimetres.
    /// A course that follows the form no longer lines up with an axis-aligned grid, so the
    /// footprint has to be mapped polygon and all.
    /// </summary>
    public static LinearRgb[] Sample(
        SourceImage image, CropRect crop, MosaicLayout layout, IReadOnlyList<Tessera> tesserae)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(tesserae);

        double fieldWidth = layout.FieldWidthMm;
        double fieldHeight = layout.FieldHeightMm;

        var cells = new LinearRgb[tesserae.Count];
        for (int i = 0; i < tesserae.Count; i++)
        {
            Tessera tessera = tesserae[i];
            var pixels = new PointD[tessera.Polygon.Length];

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (int p = 0; p < pixels.Length; p++)
            {
                double sx = crop.X + (tessera.Polygon[p].X / fieldWidth * crop.Width);
                double sy = crop.Y + (tessera.Polygon[p].Y / fieldHeight * crop.Height);
                pixels[p] = new PointD(sx, sy);
                minX = Math.Min(minX, sx);
                minY = Math.Min(minY, sy);
                maxX = Math.Max(maxX, sx);
                maxY = Math.Max(maxY, sy);
            }

            cells[i] = AveragePolygon(image, pixels, minX, minY, maxX, maxY, crop);
        }

        return cells;
    }

    /// <summary>Spread in ΔE units above which a tessera is taken to straddle an edge, not to shade smoothly.</summary>
    private const double EdgeSpread = 9.0;

    private static LinearRgb AveragePolygon(
        SourceImage image, PointD[] pixels, double minX, double minY, double maxX, double maxY, CropRect crop)
    {
        int x0 = Math.Clamp((int)Math.Floor(minX), crop.X, crop.X + crop.Width - 1);
        int y0 = Math.Clamp((int)Math.Floor(minY), crop.Y, crop.Y + crop.Height - 1);
        int x1 = Math.Clamp((int)Math.Ceiling(maxX), x0 + 1, crop.X + crop.Width);
        int y1 = Math.Clamp((int)Math.Ceiling(maxY), y0 + 1, crop.Y + crop.Height);

        int capacity = (x1 - x0) * (y1 - y0);
        var lin = new LinearRgb[capacity];
        var lab = new CieLab[capacity];
        int count = 0;

        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                if (!FieldGeometry.Contains(pixels, x + 0.5, y + 0.5))
                {
                    continue;
                }

                LinearRgb pixel = image.GetPixel(x, y).ToLinear();
                lin[count] = pixel;
                lab[count] = pixel.ToLab();
                count++;
            }
        }

        if (count == 0)
        {
            // The footprint fell between pixel centres; take the single pixel under its middle.
            int cx = Math.Clamp((x0 + x1) / 2, 0, image.Width - 1);
            int cy = Math.Clamp((y0 + y1) / 2, 0, image.Height - 1);
            return image.GetPixel(cx, cy).ToLinear();
        }

        CieLab meanLab = Mean(lab, count);
        double spread = Spread(lab, count, meanLab);
        if (spread <= EdgeSpread)
        {
            return Mean(lin, count);
        }

        // The tessera sits on an edge: its plain average is a muddy midtone that matches nothing.
        // Split the pixels in two and hand back the dominant side, so a whisker or an eyelid keeps
        // a crisp colour instead of being blended away.
        return DominantCluster(lin, lab, count, meanLab);
    }

    private static LinearRgb DominantCluster(
        LinearRgb[] lin, CieLab[] lab, int count, CieLab meanLab)
    {
        CieLab seedA = meanLab;
        CieLab seedB = lab[0];
        double worst = -1.0;
        for (int i = 0; i < count; i++)
        {
            double d = ColorDistance.CieDe76Squared(lab[i], meanLab);
            if (d > worst)
            {
                worst = d;
                seedB = lab[i];
            }
        }

        Span<bool> inA = count <= 4096 ? stackalloc bool[count] : new bool[count];

        for (int iteration = 0; iteration < 4; iteration++)
        {
            double sumAL = 0, sumAa = 0, sumAb = 0;
            double sumBL = 0, sumBa = 0, sumBb = 0;
            int nA = 0;

            for (int i = 0; i < count; i++)
            {
                bool a = ColorDistance.CieDe76Squared(lab[i], seedA)
                    <= ColorDistance.CieDe76Squared(lab[i], seedB);
                inA[i] = a;
                if (a)
                {
                    sumAL += lab[i].L;
                    sumAa += lab[i].A;
                    sumAb += lab[i].B;
                    nA++;
                }
                else
                {
                    sumBL += lab[i].L;
                    sumBa += lab[i].A;
                    sumBb += lab[i].B;
                }
            }

            int nB = count - nA;
            if (nA == 0 || nB == 0)
            {
                break;
            }

            seedA = new CieLab(sumAL / nA, sumAa / nA, sumAb / nA);
            seedB = new CieLab(sumBL / nB, sumBa / nB, sumBb / nB);
        }

        double rA = 0, gA = 0, bA = 0, rB = 0, gB = 0, bB = 0;
        int countA = 0;
        for (int i = 0; i < count; i++)
        {
            if (inA[i])
            {
                rA += lin[i].R;
                gA += lin[i].G;
                bA += lin[i].B;
                countA++;
            }
            else
            {
                rB += lin[i].R;
                gB += lin[i].G;
                bB += lin[i].B;
            }
        }

        int countB = count - countA;
        if (countA == 0)
        {
            return new LinearRgb(rB / countB, gB / countB, bB / countB);
        }

        if (countB == 0 || countA >= countB)
        {
            return new LinearRgb(rA / countA, gA / countA, bA / countA);
        }

        return new LinearRgb(rB / countB, gB / countB, bB / countB);
    }

    private static CieLab Mean(CieLab[] values, int count)
    {
        double l = 0, a = 0, b = 0;
        for (int i = 0; i < count; i++)
        {
            l += values[i].L;
            a += values[i].A;
            b += values[i].B;
        }

        return new CieLab(l / count, a / count, b / count);
    }

    private static LinearRgb Mean(LinearRgb[] values, int count)
    {
        double r = 0, g = 0, b = 0;
        for (int i = 0; i < count; i++)
        {
            r += values[i].R;
            g += values[i].G;
            b += values[i].B;
        }

        return new LinearRgb(r / count, g / count, b / count);
    }

    private static double Spread(CieLab[] values, int count, CieLab mean)
    {
        double sum = 0;
        for (int i = 0; i < count; i++)
        {
            sum += ColorDistance.CieDe76Squared(values[i], mean);
        }

        return Math.Sqrt(sum / count);
    }

    /// <summary>Maps a span in field millimetres onto whole source pixels, never narrower than one pixel.</summary>
    private static (int Start, int End) MapSpan(
        double startMm,
        double endMm,
        double fieldMm,
        int cropOrigin,
        int cropLength)
    {
        int start = (int)Math.Floor(cropOrigin + (startMm / fieldMm * cropLength));
        int end = (int)Math.Ceiling(cropOrigin + (endMm / fieldMm * cropLength));

        start = Math.Clamp(start, cropOrigin, cropOrigin + cropLength - 1);
        end = Math.Clamp(end, start + 1, cropOrigin + cropLength);

        return (start, end);
    }
}
