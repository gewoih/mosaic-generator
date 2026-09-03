using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Imaging;

namespace MosaicGenerator.Core.Tests.Support;

internal static class ImageFactory
{
    public static SourceImage Solid(int width, int height, string hex) =>
        FromPixels(width, height, (_, _) => hex);

    public static SourceImage Checkerboard(int width, int height, string even, string odd) =>
        FromPixels(width, height, (x, y) => (x + y) % 2 == 0 ? even : odd);

    public static SourceImage FromPixels(int width, int height, Func<int, int, string> hexAt)
    {
        var rgb = new byte[width * height * 3];
        int offset = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                (byte r, byte g, byte b) = Rgb.FromHex(hexAt(x, y)).ToBytes();
                rgb[offset++] = r;
                rgb[offset++] = g;
                rgb[offset++] = b;
            }
        }

        return new SourceImage(rgb, width, height);
    }
}
