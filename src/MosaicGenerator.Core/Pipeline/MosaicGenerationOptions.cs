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
}
