using MosaicGenerator.Core.Material;

namespace MosaicGenerator.Core.Rendering;

public interface IMosaicRenderer
{
    /// <summary>
    /// Cartoon: the layout in flat article colour over the joint, for printing 1:1. A 100 mm scale
    /// bar sits under the panel to catch a printer that has rescaled.
    /// </summary>
    byte[] RenderCartoon(RenderPlan plan);

    /// <summary>Working scheme: the same grid in outline, each module carrying its colour code.</summary>
    byte[] RenderScheme(RenderPlan plan, MaterialReport report);

    /// <summary>Legend sheet: one row per article — swatch, number, code and piece count.</summary>
    byte[] RenderLegend(MaterialReport report);
}
