using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Imaging;
using MosaicGenerator.Core.Skia;
using SkiaSharp;

namespace MosaicGenerator.Core.Tests.Skia;

public class SkiaImageLoaderTests
{
    private readonly SkiaImageLoader _loader = new();
    private readonly ImageLoadLimits _limits = new();

    [Fact]
    public void LoadsAPngAtItsNativeSize()
    {
        using var stream = new MemoryStream(Encode(64, 48, SKColors.Red, SKEncodedImageFormat.Png));

        SourceImage image = _loader.Load(stream, _limits);

        Assert.Equal(64, image.Width);
        Assert.Equal(48, image.Height);
        Assert.Equal(Rgb.FromHex("#FF0000").ToBytes(), image.GetPixel(10, 10).ToBytes());
    }

    [Fact]
    public void LoadsAJpeg()
    {
        using var stream = new MemoryStream(Encode(64, 48, SKColors.White, SKEncodedImageFormat.Jpeg));

        SourceImage image = _loader.Load(stream, _limits);

        Assert.Equal(64, image.Width);
        Assert.InRange(image.GetPixel(32, 24).R, 0.95, 1.0);
    }

    [Fact]
    public void TransparentAreasAreFlattenedOntoWhiteNotOntoBlack()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(16, 16, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
        }

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());

        SourceImage loaded = _loader.Load(stream, _limits);

        Assert.Equal(Rgb.FromHex("#FFFFFF").ToBytes(), loaded.GetPixel(8, 8).ToBytes());
    }

    [Fact]
    public void AnOversizedImageIsScaledDownInsteadOfRefused()
    {
        using var stream = new MemoryStream(Encode(800, 600, SKColors.Red, SKEncodedImageFormat.Png));

        SourceImage image = _loader.Load(stream, new ImageLoadLimits { MaxDecodedPixels = 10_000 });

        Assert.True((long)image.Width * image.Height <= 10_000, $"got {image.Width}x{image.Height}");
        Assert.Equal(800.0 / 600.0, (double)image.Width / image.Height, 0.05);
    }

    [Fact]
    public void AnImageBeyondTheDeclaredCapIsRefused()
    {
        using var stream = new MemoryStream(Encode(800, 600, SKColors.Red, SKEncodedImageFormat.Png));

        InvalidImageException error = Assert.Throws<InvalidImageException>(() =>
            _loader.Load(stream, new ImageLoadLimits { MaxDeclaredPixels = 1000 }));

        Assert.Contains("слишком большое", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("MZ\x90\0this is not an image at all")]
    [InlineData("<html><body>hello</body></html>")]
    [InlineData("")]
    public void NonImageContentIsRefusedBeforeDecoding(string content)
    {
        using var stream = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(content));

        Assert.Throws<InvalidImageException>(() => _loader.Load(stream, _limits));
    }

    [Fact]
    public void APngHeaderWithTruncatedBodyIsRefused()
    {
        byte[] truncated = Encode(64, 48, SKColors.Red, SKEncodedImageFormat.Png)[..40];
        using var stream = new MemoryStream(truncated);

        Assert.Throws<InvalidImageException>(() => _loader.Load(stream, _limits));
    }

    private static byte[] Encode(int width, int height, SKColor color, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(color);
        }

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(format, 100);
        return data.ToArray();
    }
}
