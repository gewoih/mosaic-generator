using MosaicGenerator.Core.Grid;
using MosaicGenerator.Core.Rendering;

namespace MosaicGenerator.Core.Tests.Grid;

/// <summary>
/// The line two courses are knapped to where they meet. What matters is not where any one piece is
/// cut but that both sides are cut to the <em>same</em> line: two lines at the angle between the
/// courses open a wedge of adhesive between them, which is what TODO п. 4 is about
/// (<c>docs/dyry-kliny-plan.md</c>).
/// </summary>
public class SeamCutTests
{
    private const double Joint = 0.5;

    /// <summary>The cap on how far the seam may lean off a piece's own course, in degrees.</summary>
    private const double LeanCapDegrees = 3.0;

    private static (double Nx, double Ny, double Distance) Cut(
        PointD from, PointD fromTangent, PointD to, PointD toTangent) =>
        Tessellation.SeamCut(from, fromTangent, to, toTangent, Joint);

    private static (double Nx, double Ny, double Distance) Pair(double degrees, bool reversed = false)
    {
        double a = degrees * Math.PI / 180.0;
        PointD tangent = reversed
            ? new PointD(-Math.Cos(a), -Math.Sin(a))
            : new PointD(Math.Cos(a), Math.Sin(a));
        return Cut(new PointD(0.0, 0.0), new PointD(1.0, 0.0), new PointD(0.0, 8.0), tangent);
    }

    [Theory]
    [InlineData(0.0)]      // parallel courses — the plain grid, which must not move
    [InlineData(2.0)]
    [InlineData(6.0)]      // twice the cap: the last angle the seam can still be shared
    public void GentlyMeetingCoursesAreKnappedToOneLineAGroutApart(double degrees)
    {
        double a = degrees * Math.PI / 180.0;
        var here = new PointD(0.0, 0.0);
        var there = new PointD(0.0, 8.0);
        var hereTangent = new PointD(1.0, 0.0);
        var thereTangent = new PointD(Math.Cos(a), Math.Sin(a));

        (double nx, double ny, double offset) = Cut(here, hereTangent, there, thereTangent);
        (double mx, double my, double mOffset) = Cut(there, thereTangent, here, hereTangent);

        // One line seen from both sides: the normals are opposite, so the cut edges are parallel and
        // the joint between them keeps one width along the whole seam.
        Assert.Equal(-mx, nx, 1e-9);
        Assert.Equal(-my, ny, 1e-9);

        PointD On(PointD site, double dx, double dy, double d) => new(site.X + (dx * d), site.Y + (dy * d));
        PointD mine = On(here, nx, ny, offset);
        PointD theirs = On(there, mx, my, mOffset);

        Assert.Equal(2.0 * Joint, ((theirs.X - mine.X) * nx) + ((theirs.Y - mine.Y) * ny), 1e-9);
    }

    [Fact]
    public void ParallelCoursesAreCutSquareAcross()
    {
        (double nx, double ny, double offset) = Pair(0.0);

        // Nothing changes where the layout is a plain grid: the cut is square across the course, half
        // the spacing out, less the joint. This is the sunset case, and it must stay put.
        Assert.Equal(0.0, nx, 1e-9);
        Assert.Equal(1.0, ny, 1e-9);
        Assert.Equal(4.0 - Joint, offset, 1e-9);
    }

    [Fact]
    public void TheSeamLeansHalfwayBetweenTwoCoursesThatNearlyAgree()
    {
        double a = 4.0 * Math.PI / 180.0;
        (double nx, double ny, double _) = Pair(4.0);

        Assert.Equal(a / 2.0, Math.Atan2(-nx, ny), 1e-9);
    }

    [Fact]
    public void ACourseCrossingSteeplyIsCutAlmostSquareInstead()
    {
        (double nx, double ny, double _) = Pair(50.0);

        // A course meeting this one at fifty degrees is not a seam to share a line with — it is a
        // course ending against this one, and a piece knapped to half of that angle comes out a
        // wedge too narrow to cut by hand. So the lean stops at the cap.
        Assert.Equal(LeanCapDegrees * Math.PI / 180.0, Math.Atan2(-nx, ny), 1e-9);
    }

    [Fact]
    public void CoursesRunningTheOppositeWayCountAsTheSameSeam()
    {
        (double nx, double ny, double offset) = Pair(4.0);
        (double rx, double ry, double rOffset) = Pair(4.0, reversed: true);

        // A streamline's direction is arbitrary — a course laid right to left is the same course.
        Assert.Equal(nx, rx, 1e-9);
        Assert.Equal(ny, ry, 1e-9);
        Assert.Equal(offset, rOffset, 1e-9);
    }
}
