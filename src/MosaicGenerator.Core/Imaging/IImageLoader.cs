namespace MosaicGenerator.Core.Imaging;

public interface IImageLoader
{
    SourceImage Load(Stream stream, ImageLoadLimits limits);
}
