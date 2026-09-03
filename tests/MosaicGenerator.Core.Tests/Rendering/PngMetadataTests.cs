using MosaicGenerator.Core.Rendering;
using SkiaSharp;

namespace MosaicGenerator.Core.Tests.Rendering;

public class PngMetadataTests
{
    [Fact]
    public void ThePhysicalScaleRoundTrips()
    {
        byte[] png = EncodePng(32, 24);

        byte[] stamped = PngMetadata.WithPhysicalScale(png, pixelsPerMm: 11.811023622);

        double? read = PngMetadata.ReadPhysicalScale(stamped);
        Assert.NotNull(read);

        // pHYs stores whole pixels per metre, so the scale round-trips to that resolution.
        Assert.Equal(11.811, read!.Value, 1e-3);
    }

    [Fact]
    public void AStampedPngIsStillDecodable()
    {
        byte[] stamped = PngMetadata.WithPhysicalScale(EncodePng(32, 24), 11.811);

        using SKBitmap? decoded = SKBitmap.Decode(stamped);

        Assert.NotNull(decoded);
        Assert.Equal(32, decoded!.Width);
        Assert.Equal(24, decoded.Height);
    }

    [Fact]
    public void APngWithoutTheChunkReportsNoScale()
    {
        Assert.Null(PngMetadata.ReadPhysicalScale(EncodePng(8, 8)));
    }

    [Fact]
    public void NonPngInputIsRejected()
    {
        Assert.Throws<ArgumentException>(() => PngMetadata.WithPhysicalScale([0xFF, 0xD8, 0xFF], 10));
        Assert.Throws<ArgumentException>(() => PngMetadata.WithPhysicalScale([], 10));
    }

    [Fact]
    public void TheChunkIsInsertedDirectlyAfterTheHeader()
    {
        byte[] stamped = PngMetadata.WithPhysicalScale(EncodePng(8, 8), 10);

        // 8-byte signature + a 25-byte IHDR chunk.
        Assert.Equal("pHYs"u8.ToArray(), stamped[37..41]);
    }

    private static byte[] EncodePng(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.CornflowerBlue);
        }

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
