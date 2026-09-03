namespace MosaicGenerator.Core.Imaging;

public enum ImageFormat
{
    Unknown,
    Jpeg,
    Png,
    WebP,
}

/// <summary>
/// Identifies an upload by its leading bytes. The declared content type and the file extension are
/// both attacker-controlled and are never consulted.
/// </summary>
public static class ImageFormatDetector
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Riff = "RIFF"u8.ToArray();
    private static readonly byte[] WebP = "WEBP"u8.ToArray();

    /// <summary>Bytes that must be available for <see cref="Detect"/> to reach a verdict.</summary>
    public const int HeaderLength = 12;

    public static ImageFormat Detect(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return ImageFormat.Jpeg;
        }

        if (header.Length >= PngSignature.Length && header[..PngSignature.Length].SequenceEqual(PngSignature))
        {
            return ImageFormat.Png;
        }

        if (header.Length >= 12 && header[..4].SequenceEqual(Riff) && header[8..12].SequenceEqual(WebP))
        {
            return ImageFormat.WebP;
        }

        return ImageFormat.Unknown;
    }

    public static bool IsSupported(ReadOnlySpan<byte> header) => Detect(header) != ImageFormat.Unknown;
}
