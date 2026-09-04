using System.Globalization;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Grid;
using MosaicGenerator.Core.Rendering;

namespace MosaicGenerator.Diag;

/// <summary>
/// Why the courses come out short (пункт 1 TODO), and whether пункты 2 and 6 are the same defect
/// seen from two other sides. The finished tesserae cannot answer the first question — a course
/// that died after two steps and one that crossed the panel are both just polygons by then — so
/// this reads <see cref="LayoutDiagnostics"/>, which the placer fills while it works.
///
/// The link to пункты 2 and 6 is measured here rather than argued: every course has two ends, an
/// end piece is cut by neighbours on three or four sides instead of two, and the space a course
/// stopped short of is where the adhesive shows. If that is right, end pieces carry the extra
/// vertices and the wide gaps sit against them.
/// </summary>
internal static class CourseAutopsy
{
    /// <summary>What a piece is, for the purposes of blaming it.</summary>
    private enum Role
    {
        Structural,
        FillEnd,
        FillMiddle,
        Filler,
    }

    public static void Report(
        MosaicLayout layout,
        IReadOnlyList<Tessera> tesserae,
        DirectionField field,
        IReadOnlyList<PointD[]> contours)
    {
        double along = layout.ModuleWidthMm;
        IReadOnlyList<LayoutDiagnostics.Attempt> attempts = LayoutDiagnostics.Attempts;

        Console.WriteLine("  — вскрытие курсов —");
        Seeds(attempts, along);
        Blockers(attempts);
        Bites(along);
        Passes();

        Role[] roles = Roles(layout, tesserae, field, contours);
        Vertices(tesserae, roles);
        Gaps(layout, tesserae, roles);
    }

    /// <summary>
    /// Every streamline the placer tried, by where its seed came from: how many were thrown away
    /// before integration even started (the spot was taken), how many grew but came out under the
    /// minimum length, and how long the survivors actually ran. Lengths are in bites, since that is
    /// what a course is counted in.
    /// </summary>
    private static void Seeds(IReadOnlyList<LayoutDiagnostics.Attempt> attempts, double alongMm)
    {
        Console.WriteLine(
            "  посев           отсеян  попыток  принято  длина принятого (кусков)  длина отброшенного");
        foreach (LayoutDiagnostics.Seeding source in Enum.GetValues<LayoutDiagnostics.Seeding>())
        {
            var mine = attempts.Where(a => a.Source == source).ToArray();
            int rejected = LayoutDiagnostics.SeedsRejected[(int)source];
            if (mine.Length == 0 && rejected == 0)
            {
                continue;
            }

            double[] taken = [.. mine.Where(a => a.Accepted).Select(a => a.LengthMm / alongMm).Order()];
            double[] dropped = [.. mine.Where(a => !a.Accepted).Select(a => a.LengthMm / alongMm).Order()];

            Console.WriteLine(string.Create(CultureInfo.GetCultureInfo("ru-RU"),
                $"  {Name(source),-14} {rejected,6}  {mine.Length,7}  {(mine.Length == 0 ? 0.0 : taken.Length * 100.0 / mine.Length),6:0}%  " +
                $"p10 {P(taken, 0.1),4:0.0}  p50 {P(taken, 0.5),4:0.0}  p90 {P(taken, 0.9),5:0.0}   " +
                $"p50 {P(dropped, 0.5),4:0.0}  max {P(dropped, 1.0),4:0.0}"));
        }
    }

    /// <summary>
    /// What stopped the streamlines, and — for the spacing stops, which is nearly all of them —
    /// whether the thing in the way was a structural course (border, contour) or other fill. This
    /// separates "the fill is crowding itself" from "the fill cannot get away from the border".
    /// </summary>
    private static void Blockers(IReadOnlyList<LayoutDiagnostics.Attempt> attempts)
    {
        foreach (bool accepted in (bool[])[true, false])
        {
            var mine = attempts.Where(a => a.Accepted == accepted).ToArray();
            if (mine.Length == 0)
            {
                continue;
            }

            int structural = mine.Count(a => a.StoppedBy == LayoutDiagnostics.Blocker.Structural);
            int fill = mine.Count(a => a.StoppedBy == LayoutDiagnostics.Blocker.Fill);
            double[] nearest = [.. mine.Where(a => a.NearestAtSeedMm >= 0.0)
                .Select(a => a.NearestAtSeedMm).Order()];

            Console.WriteLine(string.Create(CultureInfo.GetCultureInfo("ru-RU"),
                $"  {(accepted ? "принятые " : "брошенные")} ({mine.Length,4}): упёрлись в структурный {structural * 100.0 / mine.Length,3:0}%, " +
                $"в заливку {fill * 100.0 / mine.Length,3:0}%;  до соседа в момент посева p50 {P(nearest, 0.5):0.0} мм"));
        }
    }

    /// <summary>
    /// The two ways a course comes out short in pieces: the line itself is short, or the bite it is
    /// cut at is long. The fill takes a bite of up to twice the base on calm ground, so a run of five
    /// modules can arrive as two pieces without the streamline having been cut short at all. Split by
    /// the bite actually chosen, so the two causes are told apart rather than summed.
    /// </summary>
    private static void Bites(double alongMm)
    {
        var fill = LayoutDiagnostics.Courses.Where(c => !c.Structural).ToArray();
        if (fill.Length == 0)
        {
            return;
        }

        Console.WriteLine("  курсы заливки по откусу:  курсов  длина линии (модулей)  кусков в курсе  <3 кусков");
        foreach (var group in fill.GroupBy(c => c.AlongMm).OrderBy(g => g.Key))
        {
            double[] lens = [.. group.Select(c => c.LengthMm / alongMm).Order()];
            double[] sites = [.. group.Select(c => (double)c.Sites).Order()];
            Console.WriteLine(string.Create(CultureInfo.GetCultureInfo("ru-RU"),
                $"  откус {group.Key,4:0} мм            {group.Count(),5}   p10 {P(lens, 0.1),4:0.0} p50 {P(lens, 0.5),4:0.0} p90 {P(lens, 0.9),5:0.0}   " +
                $"p50 {P(sites, 0.5),4:0} p90 {P(sites, 0.9),4:0}   {group.Count(c => c.Sites < 3) * 100.0 / group.Count(),4:0}%"));
        }
    }

    /// <summary>
    /// The merge-and-grow passes of step 5. A pass that merges slivers opens holes, and a hole is
    /// only offered a course of its own on the first pass — everything later is a single filler by
    /// construction. This says how much of the filler count that rule alone accounts for.
    /// </summary>
    private static void Passes()
    {
        foreach (LayoutDiagnostics.Repair r in LayoutDiagnostics.Repairs)
        {
            Console.WriteLine(
                $"  шаг 5, проход {r.Pass}: слито {r.Merged,4}  дыр {r.Holes,5}  " +
                $"выращено курсов {r.GrownCourses,3}  филлеров {r.Fillers,4}");
        }
    }

    /// <summary>
    /// Which piece is what. Border and contour courses are laid first and hold the lowest ids;
    /// a filler has no course at all; the rest is fill, split into the two pieces at the ends of
    /// each course and everything between them.
    /// </summary>
    private static Role[] Roles(
        MosaicLayout layout,
        IReadOnlyList<Tessera> tesserae,
        DirectionField field,
        IReadOnlyList<PointD[]> contours)
    {
        int structural = Tessellation.StructuralCourseCount(layout, field, contours);
        var extremes = tesserae
            .Where(t => t.CourseId >= structural)
            .GroupBy(t => t.CourseId)
            .ToDictionary(
                g => g.Key,
                g => (Min: g.Min(t => t.IndexInCourse), Max: g.Max(t => t.IndexInCourse)));

        var roles = new Role[tesserae.Count];
        for (int i = 0; i < tesserae.Count; i++)
        {
            Tessera t = tesserae[i];
            roles[i] = t.CourseId < 0 ? Role.Filler
                : t.CourseId < structural ? Role.Structural
                : extremes[t.CourseId] is var (min, max)
                    && (t.IndexInCourse == min || t.IndexInCourse == max) ? Role.FillEnd
                : Role.FillMiddle;
        }

        return roles;
    }

    /// <summary>
    /// Пункт 2 against пункт 1: vertex counts by role. A four-sided piece is what nippers make; the
    /// question is whether the five- and six-sided ones are spread evenly or sit at the ends of
    /// courses, where a piece is cut by neighbours on three or four sides instead of two.
    /// </summary>
    private static void Vertices(IReadOnlyList<Tessera> tesserae, Role[] roles)
    {
        Console.WriteLine("  вершины куска по роли:  кусков    4      5      6     7+   среднее");
        foreach (Role role in Enum.GetValues<Role>())
        {
            int[] verts = [.. tesserae.Where((_, i) => roles[i] == role).Select(t => t.Polygon.Length)];
            if (verts.Length == 0)
            {
                continue;
            }

            double Share(Func<int, bool> p) => verts.Count(p) * 100.0 / verts.Length;
            Console.WriteLine(string.Create(CultureInfo.GetCultureInfo("ru-RU"),
                $"  {Name(role),-22} {verts.Length,6}  {Share(v => v == 4),4:0}%  {Share(v => v == 5),4:0}%  " +
                $"{Share(v => v == 6),4:0}%  {Share(v => v >= 7),4:0}%   {verts.Average(),5:0.00}"));
        }
    }

    /// <summary>
    /// Пункт 6 against пункт 1: where the wide gaps are. For every bare point sitting in a gap wider
    /// than 3 mm, the role of the nearest piece — against the share that role holds of the panel. A
    /// ratio above one means gaps gather there; the course ends and the fillers are the suspects.
    /// </summary>
    private static void Gaps(MosaicLayout layout, IReadOnlyList<Tessera> tesserae, Role[] roles)
    {
        IReadOnlyList<PointD> wide = CoverageMask.WideGapPoints(layout, tesserae, 3.0);
        if (wide.Count == 0)
        {
            Console.WriteLine("  зазоров > 3 мм нет");
            return;
        }

        var counts = new Dictionary<Role, int>();
        foreach (PointD p in wide)
        {
            int best = -1;
            double bestSq = double.MaxValue;
            for (int i = 0; i < tesserae.Count; i++)
            {
                double dx = tesserae[i].Centroid.X - p.X;
                double dy = tesserae[i].Centroid.Y - p.Y;
                double sq = (dx * dx) + (dy * dy);
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = i;
                }
            }

            counts[roles[best]] = counts.GetValueOrDefault(roles[best]) + 1;
        }

        Console.WriteLine($"  зазоры > 3 мм ({wide.Count} точек растра), ближайший кусок:");
        foreach (Role role in Enum.GetValues<Role>())
        {
            double share = roles.Count(r => r == role) * 100.0 / roles.Length;
            if (share == 0.0)
            {
                continue;
            }

            double got = counts.GetValueOrDefault(role) * 100.0 / wide.Count;
            Console.WriteLine(string.Create(CultureInfo.GetCultureInfo("ru-RU"),
                $"  {Name(role),-22} {got,5:0}% зазоров при {share,5:0}% кусков  ×{got / share,4:0.0}"));
        }
    }

    private static double P(double[] sorted, double q) =>
        sorted.Length == 0 ? 0.0 : sorted[Math.Clamp((int)(q * (sorted.Length - 1)), 0, sorted.Length - 1)];

    private static string Name(LayoutDiagnostics.Seeding s) => s switch
    {
        LayoutDiagnostics.Seeding.Structural => "от структуры",
        LayoutDiagnostics.Seeding.Offset => "офсетный",
        LayoutDiagnostics.Seeding.Sweep => "подметающий",
        _ => "в дыру",
    };

    private static string Name(Role r) => r switch
    {
        Role.Structural => "структурный курс",
        Role.FillEnd => "торец курса заливки",
        Role.FillMiddle => "середина курса",
        _ => "филлер",
    };
}
