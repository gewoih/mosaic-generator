using MosaicGenerator.Core.Imaging;
using SkiaSharp;

namespace MosaicGenerator.Core.Skia;

public sealed class SkiaImageLoader : IImageLoader
{
    public SourceImage Load(Stream stream, ImageLoadLimits limits)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(limits);

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        byte[] bytes = buffer.ToArray();

        if (bytes.Length < ImageFormatDetector.HeaderLength ||
            !ImageFormatDetector.IsSupported(bytes.AsSpan(0, ImageFormatDetector.HeaderLength)))
        {
            throw new InvalidImageException("Файл не является изображением JPEG, PNG или WebP.");
        }

        using SKData data = SKData.CreateCopy(bytes);
        using SKCodec? codec = SKCodec.Create(data);
        if (codec is null)
        {
            throw new InvalidImageException("Не удалось прочитать изображение — файл повреждён.");
        }

        SKImageInfo info = codec.Info;
        long declared = (long)info.Width * info.Height;
        if (info.Width <= 0 || info.Height <= 0)
        {
            throw new InvalidImageException("Изображение имеет нулевой размер.");
        }

        if (declared > limits.MaxDeclaredPixels)
        {
            throw new InvalidImageException(
                $"Изображение слишком большое: {info.Width}x{info.Height}. " +
                $"Максимум {limits.MaxDeclaredPixels / 1_000_000} мегапикселей.");
        }

        using SKBitmap decoded = DecodeWithinBudget(codec, info, declared, limits.MaxDecodedPixels);
        (int width, int height) = FitWithin(decoded.Width, decoded.Height, limits.MaxDecodedPixels);

        return Flatten(decoded, width, height);
    }

    /// <summary>
    /// JPEG can be decoded straight to a fraction of its size in the DCT domain, which avoids
    /// materialising the full-resolution bitmap for an oversized upload. Other formats decode whole.
    /// </summary>
    private static SKBitmap DecodeWithinBudget(SKCodec codec, SKImageInfo info, long declared, long budget)
    {
        SKImageInfo target = info
            .WithColorType(SKColorType.Rgba8888)
            .WithAlphaType(SKAlphaType.Unpremul);

        if (declared > budget)
        {
            SKSizeI scaled = codec.GetScaledDimensions((float)Math.Sqrt((double)budget / declared));
            if (scaled.Width > 0 && scaled.Height > 0 &&
                (long)scaled.Width * scaled.Height < declared)
            {
                target = target.WithSize(scaled.Width, scaled.Height);
            }
        }

        return SKBitmap.Decode(codec, target)
            ?? throw new InvalidImageException("Не удалось декодировать изображение.");
    }

    private static (int Width, int Height) FitWithin(int width, int height, long budget)
    {
        long pixels = (long)width * height;
        if (pixels <= budget)
        {
            return (width, height);
        }

        double scale = Math.Sqrt((double)budget / pixels);
        return (Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
    }

    /// <summary>
    /// Composites onto white and, if the source is still over budget, resamples in one step.
    /// Transparent regions would otherwise average in as black. Any resampling here happens in the
    /// encoded space, but it only ever runs on very large uploads and at a small ratio; the cell
    /// averages that actually drive the palette choice are computed in linear light downstream.
    /// </summary>
    private static SourceImage Flatten(SKBitmap decoded, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var target = new SKBitmap(info);
        using (var canvas = new SKCanvas(target))
        {
            canvas.Clear(SKColors.White);
            using SKImage image = SKImage.FromBitmap(decoded);
            canvas.DrawImage(
                image,
                new SKRect(0, 0, width, height),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
                paint: null);
        }

        return SourceImage.FromInterleaved(
            target.GetPixelSpan(),
            width,
            height,
            target.RowBytes,
            bytesPerPixel: 4,
            redOffset: 0,
            greenOffset: 1,
            blueOffset: 2);
    }
}
