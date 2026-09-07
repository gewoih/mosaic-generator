using MosaicGenerator.Core.Imaging;
using MosaicGenerator.Core.Rendering;
using MosaicGenerator.Core.Validation;

namespace MosaicGenerator.Core.Pipeline;

public sealed record MosaicGenerationOptions
{
    public ImageLoadLimits ImageLimits { get; init; } = new();

    public ValidationLimits ValidationLimits { get; init; } = new();

    public RenderOptions Cartoon { get; init; } = RenderOptions.Cartoon;

    public RenderOptions Scheme { get; init; } = RenderOptions.Scheme;

    /// <summary>Edge-preserving flattening before sampling. Null = off.</summary>
    public FlattenSettings? Flatten { get; init; }

    /// <summary>
    /// Render a cartoon and a material table for every shade count around the automatic pick, so
    /// the result page can step through them without a round trip. The diagnostic bench turns this
    /// off — it wants the auto number and the one cartoon, not eleven.
    /// </summary>
    public bool BakeColorLadder { get; init; } = true;
}
