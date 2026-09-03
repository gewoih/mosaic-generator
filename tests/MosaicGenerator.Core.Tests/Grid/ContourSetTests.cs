using MosaicGenerator.Core.Grid;
using MosaicGenerator.Core.Imaging;
using MosaicGenerator.Core.Rendering;
using MosaicGenerator.Core.Tests.Support;

namespace MosaicGenerator.Core.Tests.Grid;

public class ContourSetTests
{
    // A grey disc whose lightness matches the grey-blue surround: there is no luminance edge to
    // trace around it, only a chroma one. The figure mask still separates them, so the silhouette
    // comes out as one continuous ring rather than a handful of fragments.
    [Fact]
    public void SilhouetteIsOneClosedRingEvenWhereToneMatchesTheSurround()
    {
        const int size = 240;
        const double radius = 72.0;
        SourceImage image = ImageFactory.FromPixels(size, size, (x, y) =>
        {
            double dx = (x - (size / 2.0)) / 1.15;
            double dy = (y - (size * 0.45)) / 0.9;
            return (dx * dx) + (dy * dy) <= radius * radius ? "#b2b2b2" : "#a8b6cc";
        });

        // Roughly the resolution the real pipeline runs a panel of this size at.
        DirectionField field = DirectionField.Compute(
            image, new CropRect(0, 0, size, size), fieldAspect: 1.0, longSide: 200);
        IReadOnlyList<PointD[]> contours = ContourSet.Extract(field, size, size, moduleMm: 4.0);

        Assert.NotEmpty(contours);

        PointD[] silhouette = contours[0];
        double perimeter = Math.PI * (radius * 1.15 + radius * 0.9);

        Assert.True(
            Geometry.PolylineLength(silhouette) > 0.7 * perimeter,
            $"silhouette {Geometry.PolylineLength(silhouette):0} mm, disc perimeter {perimeter:0} mm");

        double gap = Distance(silhouette[0], silhouette[^1]);
        Assert.True(gap < 8.0, $"ring should close, endpoints {gap:0.0} mm apart");
    }

    [Fact]
    public void NoCleanFigureFallsBackToTheEdgeLevelSet()
    {
        // Left half dark, right half light: one straight edge, no enclosed subject. The perimeter
        // band carries both tones, so the figure mask bails; Extract still returns the level-set
        // contour, and it must not be a closed ring.
        SourceImage image = ImageFactory.FromPixels(200, 200, (x, _) => x < 100 ? "#202020" : "#d8d8d8");

        DirectionField field = DirectionField.Compute(
            image, new CropRect(0, 0, 200, 200), fieldAspect: 1.0, longSide: 200);
        IReadOnlyList<PointD[]> contours = ContourSet.Extract(field, 200, 200, moduleMm: 4.0);

        Assert.NotEmpty(contours);
        Assert.All(contours, c => Assert.True(
            Distance(c[0], c[^1]) > 20.0, "a straight edge must not chain into a closed ring"));
    }

    private static double Distance(PointD a, PointD b) =>
        Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));
}
