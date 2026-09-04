namespace MosaicGenerator.Core.Grid;

/// <summary>
/// The layout's diagnostic channel: why a course stopped where it did, and what became of the space
/// it did not cover. Nothing in the pipeline reads any of this — it exists so
/// <c>tools/MosaicGenerator.Diag</c> can ask the placer questions the finished tesserae cannot
/// answer, because by then a course that died after two steps and a course that ran the width of
/// the panel look the same: a list of polygons.
///
/// Off by default and written only under <see cref="Enabled"/>, so production pays one boolean test
/// per streamline. The bench runs one panel at a time; the counters are guarded all the same, since
/// a stray parallel run would otherwise corrupt them silently rather than fail.
/// </summary>
public static class LayoutDiagnostics
{
    private static readonly Lock Gate = new();
    private static readonly List<Attempt> AttemptLog = [];
    private static readonly List<Repair> RepairLog = [];
    private static readonly List<Course> CourseLog = [];

    /// <summary>Where the seed a streamline grew from came from.</summary>
    public enum Seeding
    {
        /// <summary>Off a border ring or a contour — the structural courses, laid before the fill.</summary>
        Structural,

        /// <summary>A course spacing to either side of a streamline that has just been accepted.</summary>
        Offset,

        /// <summary>The uniform sweep over the field that closes what the offsets missed.</summary>
        Sweep,

        /// <summary>Grown into a bare spot in step 5.</summary>
        Hole,
    }

    /// <summary>Why one half of a streamline stopped growing.</summary>
    public enum Stop
    {
        /// <summary>Ran to the edge of the field.</summary>
        Edge,

        /// <summary>Came within the test distance of an already-laid course or barrier.</summary>
        Spacing,

        /// <summary>Curled back onto itself.</summary>
        SelfHit,

        /// <summary>Held the tightest bend the glass allows for longer than the budget.</summary>
        Curvature,

        /// <summary>Ran out of integration steps.</summary>
        Steps,
    }

    /// <summary>What kind of point a streamline stopped against.</summary>
    public enum Blocker
    {
        None,

        /// <summary>A border ring or a contour course.</summary>
        Structural,

        /// <summary>Another fill streamline, or one grown into a hole.</summary>
        Fill,
    }

    /// <summary>
    /// One streamline the placer tried to grow. <paramref name="LengthMm"/> is what it reached
    /// before both halves stopped; <paramref name="Accepted"/> says whether that cleared the
    /// minimum length and became a course, or was thrown away.
    /// </summary>
    public sealed record Attempt(
        Seeding Source,
        bool Accepted,
        double LengthMm,
        double MinLengthMm,
        Stop Forward,
        Stop Backward,
        Blocker StoppedBy,
        double NearestAtSeedMm);

    /// <summary>One pass of the merge-and-grow correction in step 5.</summary>
    public sealed record Repair(int Pass, int Merged, int Holes, int GrownCourses, int Fillers);

    /// <summary>
    /// One course as it was cut into sites: how long the line ran, the bite it was cut at, and how
    /// many pieces that came to. The three are not interchangeable — a course of five modules cut at
    /// a doubled bite is a course of two pieces — and the difference is invisible in the finished
    /// tesserae.
    /// </summary>
    public sealed record Course(int Id, double LengthMm, double AlongMm, int Sites, bool Structural);

    /// <summary>Whether the placer records anything at all. The bench sets this; production never does.</summary>
    public static bool Enabled { get; set; }

    /// <summary>
    /// Why streamlines stopped, in the order of <see cref="Stop"/>. Counted whether or not
    /// <see cref="Enabled"/> is set — it is five increments per panel's worth of courses — because
    /// the bench has printed this line since before there was a diagnostic channel to put it in.
    /// </summary>
    public static readonly int[] BreakReasons = new int[5];

    /// <summary>Seeds thrown away before any integration, because they landed on an occupied spot.</summary>
    public static readonly int[] SeedsRejected = new int[4];

    /// <summary>Every streamline the placer tried to grow, in the order it tried them.</summary>
    public static IReadOnlyList<Attempt> Attempts
    {
        get
        {
            lock (Gate)
            {
                return [.. AttemptLog];
            }
        }
    }

    /// <summary>The merge-and-grow passes of step 5, in order.</summary>
    public static IReadOnlyList<Repair> Repairs
    {
        get
        {
            lock (Gate)
            {
                return [.. RepairLog];
            }
        }
    }

    /// <summary>Every course as it was cut, in the order it was laid.</summary>
    public static IReadOnlyList<Course> Courses
    {
        get
        {
            lock (Gate)
            {
                return [.. CourseLog];
            }
        }
    }

    public static void Reset()
    {
        lock (Gate)
        {
            Array.Clear(BreakReasons);
            Array.Clear(SeedsRejected);
            AttemptLog.Clear();
            RepairLog.Clear();
            CourseLog.Clear();
        }
    }

    internal static void Record(Attempt attempt)
    {
        if (!Enabled)
        {
            return;
        }

        lock (Gate)
        {
            AttemptLog.Add(attempt);
        }
    }

    internal static void Record(Repair repair)
    {
        if (!Enabled)
        {
            return;
        }

        lock (Gate)
        {
            RepairLog.Add(repair);
        }
    }

    internal static void Record(Course course)
    {
        if (!Enabled)
        {
            return;
        }

        lock (Gate)
        {
            CourseLog.Add(course);
        }
    }

    internal static void SeedRejected(Seeding source)
    {
        if (Enabled)
        {
            Interlocked.Increment(ref SeedsRejected[(int)source]);
        }
    }

    internal static void Broke(Stop reason) => Interlocked.Increment(ref BreakReasons[(int)reason]);
}
