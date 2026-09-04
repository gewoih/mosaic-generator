namespace MosaicGenerator.Core.Rendering;

public sealed record RenderOptions
{
    /// <summary>
    /// Output pixels per grid step. Resolution is expressed per module rather than in dpi:
    /// the layout is fixed by the panel dimensions in millimetres, and dpi only decides how
    /// many pixels each module gets.
    /// </summary>
    public int PixelsPerStep { get; init; } = 48;

    public int MaxLongSidePx { get; init; } = 6000;

    public long MaxTotalPixels { get; init; } = 30_000_000;

    /// <summary>
    /// Cartoon: the layout in flat article colour over the joint, printed 1:1 and set on
    /// directly. No tone or gloss variation — two pieces of one article must read as one
    /// colour, or the zones cannot be told apart on the print.
    /// </summary>
    public static RenderOptions Cartoon { get; } = new()
    {
        PixelsPerStep = 96,
    };

    /// <summary>
    /// Working scheme: the same courses in clean outline, each module carrying its colour
    /// code. For reading back what the layout algorithm did, not for setting from.
    /// </summary>
    public static RenderOptions Scheme { get; } = new()
    {
        PixelsPerStep = 48,
    };
}
