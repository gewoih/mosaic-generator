namespace MosaicGenerator.Core.Colors;

/// <summary>Linear-light sRGB primaries with components in [0, 1].</summary>
public readonly record struct LinearRgb(double R, double G, double B)
{
    public static LinearRgb operator +(LinearRgb a, LinearRgb b) => new(a.R + b.R, a.G + b.G, a.B + b.B);

    public static LinearRgb operator *(LinearRgb a, double factor) => new(a.R * factor, a.G * factor, a.B * factor);
}
