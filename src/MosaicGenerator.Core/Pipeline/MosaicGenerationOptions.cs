using MosaicGenerator.Core.Imaging;
using MosaicGenerator.Core.Rendering;
using MosaicGenerator.Core.Validation;

namespace MosaicGenerator.Core.Pipeline;

public sealed record MosaicGenerationOptions
{
    public ImageLoadLimits ImageLimits { get; init; } = new();

    public ValidationLimits ValidationLimits { get; init; } = new();

    public RenderOptions Preview { get; init; } = RenderOptions.Preview;

    public RenderOptions Scheme { get; init; } = RenderOptions.Scheme;
}
