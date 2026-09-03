namespace MosaicGenerator.Core.Imaging;

public readonly record struct CropRect(int X, int Y, int Width, int Height)
{
    public double Aspect => (double)Width / Height;
}
