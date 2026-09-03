using MosaicGenerator.Core.Material;

namespace MosaicGenerator.Core.Rendering;

public interface IMosaicRenderer
{
    /// <summary>Realistic preview: chipped tesserae of palette colours over the grout.</summary>
    byte[] RenderPreview(RenderPlan plan);

    /// <summary>Working scheme: the same grid in outline, each module carrying its colour code.</summary>
    byte[] RenderScheme(RenderPlan plan, MaterialReport report);
}
