namespace MosaicGenerator.Core.Colors;

/// <summary>
/// Conversions between sRGB, linear-light RGB, CIE XYZ and CIE L*a*b*.
/// All conversions assume the sRGB primaries and the D65 white point.
/// </summary>
public static class ColorSpace
{
    private const double SrgbToLinearThreshold = 0.04045;

    // Derived rather than taken as the standard's rounded 0.0031308: the two constants must be
    // exact images of each other, otherwise a value sitting on the breakpoint takes the linear
    // segment one way and the power segment the other, and the round trip drifts.
    private const double LinearToSrgbThreshold = SrgbToLinearThreshold / 12.92;

    private const double LabDelta = 6.0 / 29.0;
    private static readonly double LabDeltaCubed = LabDelta * LabDelta * LabDelta;
    private static readonly double LabSlope = 3.0 * LabDelta * LabDelta;
    private const double LabIntercept = 4.0 / 29.0;

    public static double SrgbToLinear(double component) =>
        component <= SrgbToLinearThreshold
            ? component / 12.92
            : Math.Pow((component + 0.055) / 1.055, 2.4);

    public static double LinearToSrgb(double component) =>
        component <= LinearToSrgbThreshold
            ? component * 12.92
            : (1.055 * Math.Pow(component, 1.0 / 2.4)) - 0.055;

    public static LinearRgb ToLinear(this Rgb rgb) =>
        new(SrgbToLinear(rgb.R), SrgbToLinear(rgb.G), SrgbToLinear(rgb.B));

    public static Rgb ToSrgb(this LinearRgb linear) =>
        new(LinearToSrgb(linear.R), LinearToSrgb(linear.G), LinearToSrgb(linear.B));

    public static CieXyz ToXyz(this LinearRgb c) => new(
        (0.4124564 * c.R) + (0.3575761 * c.G) + (0.1804375 * c.B),
        (0.2126729 * c.R) + (0.7151522 * c.G) + (0.0721750 * c.B),
        (0.0193339 * c.R) + (0.1191920 * c.G) + (0.9503041 * c.B));

    public static LinearRgb ToLinearRgb(this CieXyz c) => new(
        (3.2404542 * c.X) - (1.5371385 * c.Y) - (0.4985314 * c.Z),
        (-0.9692660 * c.X) + (1.8760108 * c.Y) + (0.0415560 * c.Z),
        (0.0556434 * c.X) - (0.2040259 * c.Y) + (1.0572252 * c.Z));

    public static CieLab ToLab(this CieXyz xyz)
    {
        double fx = Forward(xyz.X / CieXyz.D65White.X);
        double fy = Forward(xyz.Y / CieXyz.D65White.Y);
        double fz = Forward(xyz.Z / CieXyz.D65White.Z);

        return new CieLab((116.0 * fy) - 16.0, 500.0 * (fx - fy), 200.0 * (fy - fz));
    }

    public static CieXyz ToXyz(this CieLab lab)
    {
        double fy = (lab.L + 16.0) / 116.0;
        double fx = fy + (lab.A / 500.0);
        double fz = fy - (lab.B / 200.0);

        return new CieXyz(
            Inverse(fx) * CieXyz.D65White.X,
            Inverse(fy) * CieXyz.D65White.Y,
            Inverse(fz) * CieXyz.D65White.Z);
    }

    public static CieLab ToLab(this Rgb rgb) => rgb.ToLinear().ToXyz().ToLab();

    public static CieLab ToLab(this LinearRgb linear) => linear.ToXyz().ToLab();

    public static Rgb ToRgb(this CieLab lab) => lab.ToXyz().ToLinearRgb().ToSrgb();

    private static double Forward(double ratio) =>
        ratio > LabDeltaCubed ? Math.Cbrt(ratio) : (ratio / LabSlope) + LabIntercept;

    private static double Inverse(double f) =>
        f > LabDelta ? f * f * f : (f - LabIntercept) * LabSlope;
}
