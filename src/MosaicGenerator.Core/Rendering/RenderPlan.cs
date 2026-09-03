using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;

namespace MosaicGenerator.Core.Rendering;

public sealed record RenderPlan
{
    public required int PixelWidth { get; init; }

    public required int PixelHeight { get; init; }

    /// <summary>Output pixels per millimetre of panel. Written into the PNG so a 100% print is true to size.</summary>
    public required double PixelsPerMm { get; init; }

    public required MosaicLayout Layout { get; init; }

    public required IReadOnlyList<RenderedModule> Modules { get; init; }

    /// <summary>Side of the median tessera in output pixels — sets the scheme's stroke and font.</summary>
    public required double TesseraPixels { get; init; }

    /// <summary>
    /// What the joint reads as: white adhesive darkened by how deep it sits between the tesserae.
    /// Computed from the layout rather than chosen, so it tracks the module instead of being a
    /// swatch someone picked.
    /// </summary>
    public required Rgb JointColor { get; init; }

    /// <summary>
    /// Resolution of the raster itself. Because the physical scale is stamped into the PNG, a
    /// 100% print always comes out at the panel's real size — this is how finely it is drawn,
    /// not a reduction factor.
    /// </summary>
    public double Dpi => PixelsPerMm * 25.4;
}
