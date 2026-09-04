using MosaicGenerator.Core.Colors;

namespace MosaicGenerator.Core.Rendering;

public sealed record RenderOptions
{
    /// <summary>
    /// The gap between tesserae, as the cartoon draws it. Smalt is bedded in white adhesive and
    /// left ungrouted, so the joint is not a colour anyone picks: it is the bed seen at the bottom
    /// of a narrow slot, and very little light reaches down there. Beside the 7 mm smalt of the
    /// ArtWorker range, at the 1 mm joint every working size lands on, that reads as this dark
    /// grey — L* 40 against roughly 91 for the same adhesive in the open.
    ///
    /// A constant, not a formula. The optics that derived it — slot form factor, bed depth,
    /// adhesive white — went with the joint compensation they were written for; at every size this
    /// tool is used for the joint is 1 mm, so the formula only ever returned this one value.
    /// See docs/tsvetnoy-obodok-plan.md.
    /// </summary>
    public Rgb JointColor { get; init; } = Rgb.FromHex("#5E5E5B");

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
