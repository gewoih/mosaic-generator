namespace MosaicGenerator.Core.Rendering;

public readonly record struct PointD(double X, double Y);

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;
}

public static class Geometry
{
    /// <summary>Total length of an open polyline, in the units its points are given in.</summary>
    public static double PolylineLength(IReadOnlyList<PointD> polyline)
    {
        ArgumentNullException.ThrowIfNull(polyline);

        double total = 0.0;
        for (int i = 1; i < polyline.Count; i++)
        {
            double dx = polyline[i].X - polyline[i - 1].X;
            double dy = polyline[i].Y - polyline[i - 1].Y;
            total += Math.Sqrt((dx * dx) + (dy * dy));
        }

        return total;
    }
}
