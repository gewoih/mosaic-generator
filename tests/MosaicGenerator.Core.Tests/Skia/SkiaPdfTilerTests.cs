using MosaicGenerator.Core.Rendering;
using MosaicGenerator.Core.Skia;
using SkiaSharp;

namespace MosaicGenerator.Core.Tests.Skia;

public class SkiaPdfTilerTests
{
    [Fact]
    public void ProducesAGuidePagePlusOnePagePerSheet()
    {
        byte[] png = Raster(widthMm: 400, heightMm: 400, pixelsPerMm: 12);

        byte[] pdf = SkiaPdfTiler.Render(png, "Картон");

        Assert.Equal("%PDF"u8.ToArray(), pdf.AsSpan(0, 4).ToArray());
        SheetTiling tiling = SheetTiling.Plan(4800, 4800, 12);
        Assert.Equal(tiling.Tiles.Count + 1, CountPages(pdf));
    }

    [Fact]
    public void APanelInsideOneA4IsASinglePageWithNoGuide()
    {
        byte[] png = Raster(widthMm: 180, heightMm: 240, pixelsPerMm: 12);

        byte[] pdf = SkiaPdfTiler.Render(png, "Схема");

        Assert.Equal(1, CountPages(pdf));
    }

    [Fact]
    public void APngWithoutAScaleIsRefused()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);

        Assert.Throws<ArgumentException>(() => SkiaPdfTiler.Render(data.ToArray(), "Картон"));
    }

    private static byte[] Raster(double widthMm, double heightMm, double pixelsPerMm)
    {
        int w = (int)Math.Round(widthMm * pixelsPerMm);
        int h = (int)Math.Round(heightMm * pixelsPerMm);

        using var bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.SlateGray };
            canvas.DrawRect(0, 0, w / 2f, h / 2f, paint);
        }

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return PngMetadata.WithPhysicalScale(data.ToArray(), pixelsPerMm);
    }

    private static int CountPages(byte[] pdf)
    {
        string text = System.Text.Encoding.Latin1.GetString(pdf);
        int count = 0;
        int at = 0;
        while ((at = text.IndexOf("/Type /Page", at, StringComparison.Ordinal)) >= 0)
        {
            if (!text.AsSpan(at).StartsWith("/Type /Pages"))
            {
                count++;
            }

            at += "/Type /Page".Length;
        }

        return count;
    }
}
