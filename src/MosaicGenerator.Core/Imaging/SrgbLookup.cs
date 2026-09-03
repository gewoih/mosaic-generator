using MosaicGenerator.Core.Colors;

namespace MosaicGenerator.Core.Imaging;

/// <summary>
/// Maps the 256 possible 8-bit sRGB component values to linear light. Exact for 8-bit input
/// and avoids a Math.Pow per channel in the sampling loop.
/// </summary>
public static class SrgbLookup
{
    private static readonly double[] Table = BuildTable();

    public static double ToLinear(byte component) => Table[component];

    private static double[] BuildTable()
    {
        var table = new double[256];
        for (int i = 0; i < table.Length; i++)
        {
            table[i] = ColorSpace.SrgbToLinear(i / 255.0);
        }

        return table;
    }
}
