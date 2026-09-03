namespace MosaicGenerator.Core.Imaging;

public sealed record ImageLoadLimits
{
    /// <summary>
    /// Rejected outright above this: a header can declare a huge canvas from a tiny file, and the
    /// decoder would allocate it before anything else got a say.
    /// </summary>
    public long MaxDeclaredPixels { get; init; } = 200_000_000;

    /// <summary>Anything larger is scaled down while decoding rather than refused.</summary>
    public long MaxDecodedPixels { get; init; } = 24_000_000;
}
