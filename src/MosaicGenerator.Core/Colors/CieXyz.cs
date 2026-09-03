namespace MosaicGenerator.Core.Colors;

public readonly record struct CieXyz(double X, double Y, double Z)
{
    /// <summary>CIE 1931 tristimulus values of the D65 illuminant, 2-degree observer.</summary>
    public static readonly CieXyz D65White = new(0.95047, 1.0, 1.08883);
}
