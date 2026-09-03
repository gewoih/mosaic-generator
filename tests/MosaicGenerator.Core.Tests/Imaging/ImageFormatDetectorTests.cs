using MosaicGenerator.Core.Imaging;

namespace MosaicGenerator.Core.Tests.Imaging;

public class ImageFormatDetectorTests
{
    [Fact]
    public void RecognisesTheSupportedSignatures()
    {
        Assert.Equal(ImageFormat.Jpeg, ImageFormatDetector.Detect([0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0]));
        Assert.Equal(
            ImageFormat.Png,
            ImageFormatDetector.Detect([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0]));
        Assert.Equal(ImageFormat.WebP, ImageFormatDetector.Detect("RIFF\0\0\0\0WEBP"u8));
    }

    [Fact]
    public void AnExecutableRenamedToJpgIsRejected()
    {
        // Mach-O and PE headers, i.e. what an upload filter that trusts the extension would let in.
        Assert.Equal(ImageFormat.Unknown, ImageFormatDetector.Detect([0xCF, 0xFA, 0xED, 0xFE, 0, 0, 0, 0, 0, 0, 0, 0]));
        Assert.Equal(ImageFormat.Unknown, ImageFormatDetector.Detect("MZ\x90\0\0\0\0\0\0\0\0\0"u8));
        Assert.False(ImageFormatDetector.IsSupported("<?xml version=\"1.0\"?>"u8));
    }

    [Fact]
    public void ARiffContainerThatIsNotWebPIsRejected()
    {
        Assert.Equal(ImageFormat.Unknown, ImageFormatDetector.Detect("RIFF\0\0\0\0WAVE"u8));
    }

    [Fact]
    public void ATruncatedHeaderNeverThrows()
    {
        foreach (int length in Enumerable.Range(0, 12))
        {
            Assert.Equal(
                ImageFormat.Unknown,
                ImageFormatDetector.Detect(new byte[] { 0x89, 0x50, 0x4E }.AsSpan(0, Math.Min(length, 3))));
        }
    }
}
