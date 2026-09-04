using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Material;
using MosaicGenerator.Core.Rendering;
using SkiaSharp;

namespace MosaicGenerator.Core.Skia;

public sealed class SkiaMosaicRenderer : IMosaicRenderer
{
    private static readonly SKColor SchemeBackground = new(0xFF, 0xFF, 0xFF);
    private static readonly SKColor SchemeOutline = new(0x33, 0x33, 0x33);
    private static readonly SKColor SchemeText = new(0x11, 0x11, 0x11);

    public byte[] RenderCartoon(RenderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return Render(plan, canvas =>
        {
            canvas.Clear(ToSKColor(plan.JointColor));

            using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            using var builder = new SKPathBuilder();

            foreach (RenderedModule module in plan.Modules)
            {
                var corners = new SKPoint[module.Quad.Length];
                for (int i = 0; i < corners.Length; i++)
                {
                    corners[i] = new SKPoint((float)module.Quad[i].X, (float)module.Quad[i].Y);
                }

                builder.Reset();
                builder.AddPoly(corners, close: true);

                using SKPath path = builder.Detach();

                fill.Color = ToSKColor(module.FillColor);
                canvas.DrawPath(path, fill);
            }
        });
    }

    public byte[] RenderScheme(RenderPlan plan, MaterialReport report)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(report);

        return Render(plan, canvas =>
        {
            canvas.Clear(SchemeBackground);

            using var fill = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = SchemeBackground,
            };
            using var outline = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                Color = SchemeOutline,
                StrokeWidth = Math.Max(1f, (float)(plan.TesseraPixels * 0.03)),
            };
            using var text = new SKPaint { IsAntialias = true, Color = SchemeText };
            using var builder = new SKPathBuilder();

            using SKFont font = BuildFont(plan, report);
            float baselineShift = MeasureBaselineShift(font);

            foreach (RenderedModule module in plan.Modules)
            {
                var corners = new SKPoint[module.Quad.Length];
                for (int i = 0; i < corners.Length; i++)
                {
                    corners[i] = new SKPoint((float)module.Quad[i].X, (float)module.Quad[i].Y);
                }

                builder.Reset();
                builder.AddPoly(corners, close: true);
                using SKPath path = builder.Detach();

                canvas.DrawPath(path, fill);
                canvas.DrawPath(path, outline);

                if (report.TryGetCode(module.ColorIndex, out string code))
                {
                    canvas.DrawText(
                        code,
                        (float)module.Centroid.X,
                        (float)module.Centroid.Y + baselineShift,
                        SKTextAlign.Center,
                        font,
                        text);
                }
            }
        });
    }

    /// <summary>Largest size at which the longest code still clears the module's inner margin.</summary>
    private static SKFont BuildFont(RenderPlan plan, MaterialReport report)
    {
        string longest = report.Lines.Count == 0
            ? "0"
            : report.Lines.MaxBy(line => line.Code.Length)!.Code;

        float available = (float)(plan.TesseraPixels * 0.7);
        var font = new SKFont(SchemeFont.Typeface, (float)(plan.TesseraPixels * 0.5));

        using var probe = new SKPaint();
        float width = font.MeasureText(longest, probe);
        if (width > available && width > 0)
        {
            font.Size *= available / width;
        }

        return font;
    }

    private static float MeasureBaselineShift(SKFont font)
    {
        SKFontMetrics metrics = font.Metrics;
        return -(metrics.Ascent + metrics.Descent) / 2f;
    }

    private static byte[] Render(RenderPlan plan, Action<SKCanvas> draw)
    {
        var info = new SKImageInfo(plan.PixelWidth, plan.PixelHeight, SKColorType.Rgba8888, SKAlphaType.Opaque);

        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            draw(canvas);
        }

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG encoding failed.");

        return PngMetadata.WithPhysicalScale(encoded.ToArray(), plan.PixelsPerMm);
    }

    private static SKColor ToSKColor(Rgb rgb)
    {
        (byte r, byte g, byte b) = rgb.ToBytes();
        return new SKColor(r, g, b);
    }
}
