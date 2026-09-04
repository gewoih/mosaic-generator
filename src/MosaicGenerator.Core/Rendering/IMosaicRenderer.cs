using MosaicGenerator.Core.Material;

namespace MosaicGenerator.Core.Rendering;

public interface IMosaicRenderer
{
    /// <summary>Cartoon: the layout in flat article colour over the joint, for printing 1:1.</summary>
    byte[] RenderCartoon(RenderPlan plan);

    /// <summary>Working scheme: the same grid in outline, each module carrying its colour code.</summary>
    byte[] RenderScheme(RenderPlan plan, MaterialReport report);
}
