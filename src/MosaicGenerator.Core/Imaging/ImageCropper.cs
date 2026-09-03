namespace MosaicGenerator.Core.Imaging;

public static class ImageCropper
{
    /// <summary>
    /// Largest centred rectangle of the requested aspect ratio. Returns a rectangle rather than a
    /// copy so nothing is duplicated in memory before sampling.
    /// </summary>
    public static CropRect CenterCropToAspect(int width, int height, double targetAspect) =>
        CropToAspect(width, height, targetAspect, 0.5, 0.5);

    /// <summary>
    /// Largest rectangle of the requested aspect ratio, positioned so that the point at
    /// (<paramref name="anchorX"/>, <paramref name="anchorY"/>) of the source — each a fraction of
    /// that axis — sits at its centre, pushed back inside the frame where that would overhang.
    /// The subject of a photograph is rarely in the middle, so which part survives the crop has to
    /// be sayable.
    /// </summary>
    public static CropRect CropToAspect(
        int width, int height, double targetAspect, double anchorX, double anchorY)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetAspect);

        double sourceAspect = (double)width / height;

        int cropWidth;
        int cropHeight;

        if (sourceAspect > targetAspect)
        {
            // Source is wider than the target: keep full height, trim the sides.
            cropHeight = height;
            cropWidth = Math.Max(1, (int)Math.Round(height * targetAspect));
            cropWidth = Math.Min(cropWidth, width);
        }
        else
        {
            cropWidth = width;
            cropHeight = Math.Max(1, (int)Math.Round(width / targetAspect));
            cropHeight = Math.Min(cropHeight, height);
        }

        return new CropRect(
            Origin(width, cropWidth, anchorX),
            Origin(height, cropHeight, anchorY),
            cropWidth,
            cropHeight);
    }

    /// <summary>
    /// Window position that centres the anchor, clamped to the frame. Rounding is on the centre
    /// rather than the origin so an anchor of 0.5 lands exactly where a centre crop would.
    /// </summary>
    private static int Origin(int sourceLength, int cropLength, double anchor)
    {
        if (double.IsNaN(anchor))
        {
            anchor = 0.5;
        }

        double centre = Math.Clamp(anchor, 0.0, 1.0) * sourceLength;
        return (int)Math.Clamp(Math.Round(centre - (cropLength / 2.0)), 0, sourceLength - cropLength);
    }
}
