namespace MosaicGenerator.Core.Rendering;

public sealed record RenderOptions
{
    /// <summary>
    /// Output pixels per grid step. Resolution is expressed per module rather than in dpi:
    /// the layout is fixed by the panel dimensions in millimetres, and dpi only decides how
    /// many pixels each module gets.
    /// </summary>
    public int PixelsPerStep { get; init; } = 24;

    public int MaxLongSidePx { get; init; } = 6000;

    public long MaxTotalPixels { get; init; } = 30_000_000;

    /// <summary>
    /// Offsets alternate rows by half a step. A grid with joints aligned on both axes reads as
    /// cross-stitch; a mosaicist staggers courses so the vertical joint is never continuous.
    /// On for both outputs — the scheme has to match the layout it describes.
    /// </summary>
    public bool BrickBond { get; init; } = true;

    /// <summary>
    /// Corner displacement as a fraction of the module, signed. Hand-cut smalt has a chipped,
    /// out-of-square edge; the joint it leaves is wider here and tighter there but averages out.
    /// </summary>
    public double EdgeRoughness { get; init; } = 0.08;

    /// <summary>Per-tessera rotation, in degrees, uniform in ±this. A hand-set tessera is never square to the row.</summary>
    public double RotationJitterDeg { get; init; } = 4.0;

    /// <summary>Per-tessera size variation as a fraction, per axis. No two cuts come out the same.</summary>
    public double SizeJitter { get; init; } = 0.10;

    /// <summary>
    /// Lightness ramp across the tessera face, in L* units. Smalt is glossy and slightly domed,
    /// so a face is never one flat value — one edge catches the light, the other falls into shade.
    /// </summary>
    public double GlossJitter { get; init; } = 3.5;

    /// <summary>Per-module lightness spread, in L* units of the CIELAB range.</summary>
    public double ToneJitter { get; init; } = 4.0;

    /// <summary>Realistic preview: staggered courses, chipped and rotated tesserae, a lit face.</summary>
    public static RenderOptions Preview { get; } = new();

    /// <summary>
    /// Working scheme: the same staggered courses, but clean rectangles. Chipped outlines would
    /// fight the colour code printed inside the module and blur the cutting lines.
    /// </summary>
    public static RenderOptions Scheme { get; } = new()
    {
        PixelsPerStep = 48,
        EdgeRoughness = 0.0,
        RotationJitterDeg = 0.0,
        SizeJitter = 0.0,
        GlossJitter = 0.0,
        ToneJitter = 0.0,
    };
}
