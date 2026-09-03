using System.Globalization;

namespace MosaicGenerator.Core.Colors;

/// <summary>Non-linear sRGB with components in [0, 1].</summary>
public readonly record struct Rgb(double R, double G, double B)
{
    public static Rgb FromBytes(byte r, byte g, byte b) => new(r / 255.0, g / 255.0, b / 255.0);

    public static Rgb FromHex(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);

        ReadOnlySpan<char> span = hex.AsSpan().Trim();
        if (span.Length > 0 && span[0] == '#')
        {
            span = span[1..];
        }

        if (span.Length != 6)
        {
            throw new FormatException($"Expected a 6-digit hex color, got '{hex}'.");
        }

        return FromBytes(ParseByte(span[..2]), ParseByte(span[2..4]), ParseByte(span[4..]));

        static byte ParseByte(ReadOnlySpan<char> pair) =>
            byte.TryParse(pair, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte value)
                ? value
                : throw new FormatException($"'{pair}' is not a hex byte.");
    }

    public (byte R, byte G, byte B) ToBytes() => (ToByte(R), ToByte(G), ToByte(B));

    public string ToHex()
    {
        (byte r, byte g, byte b) = ToBytes();
        return string.Create(7, (r, g, b), static (buffer, value) =>
        {
            buffer[0] = '#';
            value.r.TryFormat(buffer[1..3], out _, "X2", CultureInfo.InvariantCulture);
            value.g.TryFormat(buffer[3..5], out _, "X2", CultureInfo.InvariantCulture);
            value.b.TryFormat(buffer[5..7], out _, "X2", CultureInfo.InvariantCulture);
        });
    }

    public Rgb Clamped() => new(Math.Clamp(R, 0, 1), Math.Clamp(G, 0, 1), Math.Clamp(B, 0, 1));

    private static byte ToByte(double component) =>
        (byte)Math.Clamp(Math.Round(component * 255.0, MidpointRounding.AwayFromZero), 0, 255);
}
