using MosaicGenerator.Core.Colors;

namespace MosaicGenerator.Core.Imaging;

/// <summary>
/// A decoded photo held as tightly packed 8-bit sRGB triplets. Storage stays in the source
/// encoding — 3 bytes per pixel rather than 12 — and every read converts to linear light on the
/// way out, so averaging always happens in linear space without materialising a float image.
/// </summary>
public sealed class SourceImage
{
    private readonly byte[] _rgb;

    public SourceImage(byte[] rgb, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(rgb);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        long expected = (long)width * height * 3;
        if (rgb.LongLength != expected)
        {
            throw new ArgumentException(
                $"Expected {expected} bytes for {width}x{height}, got {rgb.LongLength}.", nameof(rgb));
        }

        _rgb = rgb;
        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Packs an interleaved buffer (RGBA, BGRA, RGB, ...) into tightly packed RGB.</summary>
    public static SourceImage FromInterleaved(
        ReadOnlySpan<byte> source,
        int width,
        int height,
        int stride,
        int bytesPerPixel,
        int redOffset,
        int greenOffset,
        int blueOffset)
    {
        var rgb = new byte[(long)width * height * 3];
        int target = 0;

        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> row = source.Slice(y * stride, width * bytesPerPixel);
            for (int x = 0; x < width; x++)
            {
                int offset = x * bytesPerPixel;
                rgb[target++] = row[offset + redOffset];
                rgb[target++] = row[offset + greenOffset];
                rgb[target++] = row[offset + blueOffset];
            }
        }

        return new SourceImage(rgb, width, height);
    }

    public Rgb GetPixel(int x, int y)
    {
        int offset = ((y * Width) + x) * 3;
        return Rgb.FromBytes(_rgb[offset], _rgb[offset + 1], _rgb[offset + 2]);
    }

    /// <summary>
    /// Box average over a rectangle, accumulated in linear light. Averaging the sRGB values
    /// instead would darken every high-contrast area.
    /// </summary>
    public LinearRgb AverageLinear(int x, int y, int width, int height)
    {
        int x0 = Math.Clamp(x, 0, Width - 1);
        int y0 = Math.Clamp(y, 0, Height - 1);
        int x1 = Math.Clamp(x + width, x0 + 1, Width);
        int y1 = Math.Clamp(y + height, y0 + 1, Height);

        double r = 0, g = 0, b = 0;

        for (int row = y0; row < y1; row++)
        {
            int offset = ((row * Width) + x0) * 3;
            for (int column = x0; column < x1; column++)
            {
                r += SrgbLookup.ToLinear(_rgb[offset]);
                g += SrgbLookup.ToLinear(_rgb[offset + 1]);
                b += SrgbLookup.ToLinear(_rgb[offset + 2]);
                offset += 3;
            }
        }

        double count = (double)(x1 - x0) * (y1 - y0);
        return new LinearRgb(r / count, g / count, b / count);
    }
}
