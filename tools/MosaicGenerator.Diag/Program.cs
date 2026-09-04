using System.Diagnostics;
using System.Globalization;
using System.Text;
using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Grid;
using MosaicGenerator.Core.Imaging;
using MosaicGenerator.Core.Optics;
using MosaicGenerator.Core.Pipeline;
using MosaicGenerator.Core.Quantization;
using MosaicGenerator.Core.Rendering;
using MosaicGenerator.Core.Skia;

namespace MosaicGenerator.Diag;

/// <summary>
/// Runs one photograph through the whole pipeline over a matrix of panel sizes and bite lengths,
/// writes every cartoon and scheme out as PNG, and measures what the eye cannot count. A layout
/// that looks right on a synthetic disc says nothing about a photograph, and there was no way to
/// get numbers out of the web app.
/// </summary>
internal static class Program
{
    private sealed record Run(
        string Name, double WidthCm, double HeightCm, double AlongMm, double AnchorX, int MaxColors,
        bool CompensateJoint);

    private static int Main(string[] args)
    {
        string? photo = Arg(args, "--photo");
        string palettes = Arg(args, "--palettes") ?? "src/MosaicGenerator.Web/Data/palettes";
        string outDir = Arg(args, "--out") ?? "diag-out";
        string paletteId = Arg(args, "--palette") ?? "artworker-smalt";
        int[] colorCeilings = [.. (Arg(args, "--colors") ?? "20")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(c => int.Parse(c, CultureInfo.InvariantCulture))];
        (double W, double H)[] sizes = [.. (Arg(args, "--sizes") ?? "15x15,30x30,40x30,60x45,90x70,120x90")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s =>
            {
                string[] parts = s.Split('x');
                return (double.Parse(parts[0], CultureInfo.InvariantCulture),
                        double.Parse(parts[1], CultureInfo.InvariantCulture));
            })];
        double[] modules = [.. (Arg(args, "--modules")
                ?? string.Join(',', ModuleSelector.AvailableModulesMm.Select(m => m.ToString(CultureInfo.InvariantCulture))))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => double.Parse(x, CultureInfo.InvariantCulture))];
        bool[] jointModes = args.Contains("--both-joint") ? [true, false]
            : args.Contains("--nojoint") ? [false] : [true];

        // Which ΔE the palette matcher runs on for this whole invocation. Everything the bench
        // *measures* stays on CIE76, so two runs — one per metric — are scored by the same ruler.
        ColorDistance.MatchingMetric = (Arg(args, "--metric") ?? "cie76").ToLowerInvariant() switch
        {
            "cie76" => ColorDistance.Metric.Cie76,
            "ciede2000" or "de2000" or "ciede" => ColorDistance.Metric.Ciede2000,
            var other => throw new ArgumentException($"неизвестная метрика '{other}' (cie76 | ciede2000)"),
        };

        if (photo is null || !File.Exists(photo))
        {
            Console.Error.WriteLine(
                "usage: --photo <file> [--palettes <dir>] [--out <dir>] [--colors N,N] " +
                "[--sizes 15x15,30x30] [--modules 6,8,10,12,15,20] [--crops 0.5,0.35] " +
                "[--metric cie76|ciede2000]");
            return 1;
        }

        Directory.CreateDirectory(outDir);

        var repository = new JsonPaletteRepository(palettes);
        if (!repository.TryGet(paletteId, out Palette? palette))
        {
            Console.Error.WriteLine($"нет палитры '{paletteId}'");
            return 1;
        }

        var loader = new SkiaImageLoader();
        var options = new MosaicGenerationOptions();
        var service = new MosaicGenerationService(loader, new SkiaMosaicRenderer(), options);

        byte[] bytes = File.ReadAllBytes(photo);
        SourceImage image = loader.Load(new MemoryStream(bytes), options.ImageLimits);
        Console.WriteLine(
            $"фото {image.Width}×{image.Height}, палитра {palette.Name} ({palette.Colors.Count} цветов), " +
            $"метрика подбора {ColorDistance.MatchingMetric}\n");

        double[] anchors = [.. (Arg(args, "--crops") ?? "0.5")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(c => double.Parse(c, CultureInfo.InvariantCulture))];

        if (args.Contains("--gamut"))
        {
            Gamut.Report(image, palette);
            Gamut.Contrast(image, palette, colorCeilings[0]);
        }

        var runs = new List<Run>();
        foreach ((double w, double h) in sizes)
        {
            foreach (double along in modules)
            {
                foreach (int colors in colorCeilings)
                {
                    foreach (double anchor in anchors)
                    {
                        foreach (bool joint in jointModes)
                        {
                            // Invariant, and no decimal comma: a name like "21,7x29" would
                            // split the CSV row it is written into.
                            string name = string.Create(CultureInfo.InvariantCulture,
                                $"{w:0.#}x{h:0.#}-m{along:0.#}-c{colors}")
                                + (anchors.Length > 1
                                    ? string.Create(CultureInfo.InvariantCulture, $"-x{anchor:0.00}")
                                    : string.Empty)
                                + (jointModes.Length > 1 ? (joint ? "-joint" : "-nojoint") : string.Empty);
                            runs.Add(new Run(name, w, h, along, anchor, colors, joint));
                        }
                    }
                }
            }
        }

        var csv = new StringBuilder();
        csv.AppendLine(string.Join(',',
            "run", "metric", "panel", "along", "across", "grout", "acrossCount", "nominal",
            "tesserae", "ratio", "cut", "overlap", "covered", "bareR", "bare03",
            "areaMin", "areaP5", "areaMed", "areaP95", "areaMax", "tiny", "sliver", "manySided",
            "kink", "courses", "stubCourse", "medCourse", "filler", "minSideP5", "uncuttable", "awkward", "structureOff", "edgesCrossed",
            "dE_mean", "dE_p95", "dE_max", "colorsBefore", "colorsUsed", "rare", "dominant",
            "lightestGap", "banding", "merged", "reassigned", "ms"));

        foreach (Run run in runs)
        {
            ModuleChoice choice = ModuleSelector.Choose(
                run.WidthCm * 10.0, run.HeightCm * 10.0, run.AlongMm, palette.TypicalThicknessMm);

            var request = new MosaicRequest
            {
                PanelWidthMm = run.WidthCm * 10.0,
                PanelHeightMm = run.HeightCm * 10.0,
                ModuleWidthMm = choice.ModuleAlongMm,
                ModuleHeightMm = choice.ModuleAcrossMm,
                GroutWidthMm = choice.GroutMm,
                PaletteId = palette.Id,
                CropAnchorX = run.AnchorX,
                MaxColors = run.MaxColors,
                CompensateJoint = run.CompensateJoint,
            };

            var watch = Stopwatch.StartNew();
            MosaicResult result = service.Generate(new MemoryStream(bytes), request, palette);
            watch.Stop();

            File.WriteAllBytes(Path.Combine(outDir, $"{run.Name}-cartoon.png"), result.CartoonPng);
            File.WriteAllBytes(Path.Combine(outDir, $"{run.Name}-scheme.png"), result.SchemePng);

            // Recomputed rather than plumbed out of the service: every step is deterministic, so
            // this is the same layout the PNGs were drawn from.
            MosaicLayout layout = MosaicLayout.Compute(request);
            CropRect crop = ImageCropper.CropToAspect(
                image.Width, image.Height, layout.FieldAspect, request.CropAnchorX, request.CropAnchorY);
            DirectionField field = DirectionField.Compute(
                image, crop, layout.FieldAspect, DirectionField.ResolutionFor(layout));
            Array.Clear(Tessellation.BreakReasons);
            IReadOnlyList<Tessera> tesserae = Tessellation.Advected(layout, field);
            CourseGuidance guidance = CourseGuidance.Build(
                field, layout.FieldWidthMm, layout.FieldHeightMm,
                ContourSet.Extract(field, layout.FieldWidthMm, layout.FieldHeightMm, layout.ModuleWidthMm),
                layout.ModuleWidthMm, layout.ModuleHeightMm + layout.GroutWidthMm);
            int[] why = [.. Tessellation.BreakReasons];
            int whyTotal = Math.Max(1, why.Sum());
            Console.WriteLine(
                $"  обрывы курса: край {why[0] * 100.0 / whyTotal:0}%  " +
                $"расстояние {why[1] * 100.0 / whyTotal:0}%  " +
                $"самопересечение {why[2] * 100.0 / whyTotal:0}%  " +
                $"кривизна {why[3] * 100.0 / whyTotal:0}%  " +
                $"шаги {why[4] * 100.0 / whyTotal:0}%  (всего {why.Sum()})");
            LinearRgb[] cells = CellSampler.Sample(image, crop, layout, tesserae);
            int[] indices = [.. result.Scheme.Modules.Select(m => m.ColorIndex)];

            File.WriteAllText(
                Path.Combine(outDir, $"{run.Name}-indices.txt"), string.Join(',', indices));

            int[] lengths = [.. tesserae.Where(t => t.CourseId >= 0)
                .GroupBy(t => t.CourseId).Select(g => g.Count()).Order()];
            if (lengths.Length > 0)
            {
                int P(double q) => lengths[Math.Clamp((int)(q * (lengths.Length - 1)), 0, lengths.Length - 1)];
                Console.WriteLine(
                    $"  длина курса, кусков: p10 {P(0.1)}  p50 {P(0.5)}  p90 {P(0.9)}  max {lengths[^1]}  " +
                    $"курсов {lengths.Length}, из них <3 кусков {lengths.Count(l => l < 3) * 100.0 / lengths.Length:0}%  " +
                    $"кусков в курсах <5: {lengths.Where(l => l < 5).Sum() * 100.0 / lengths.Sum():0}%");

                // Weighted by pieces rather than by courses: what the length of the run under a
                // randomly chosen tessera actually is, which is what the eye reads.
                int[] byPiece = [.. lengths.SelectMany(l => Enumerable.Repeat(l, l)).Order()];
                int Q(double q) => byPiece[Math.Clamp((int)(q * (byPiece.Length - 1)), 0, byPiece.Length - 1)];
                Console.WriteLine(
                    $"  курс под случайным куском: p25 {Q(0.25)}  p50 {Q(0.5)}  p75 {Q(0.75)}  " +
                    $"филлеров {tesserae.Count(t => t.CourseId < 0) * 100.0 / tesserae.Count:0}%");
            }

            // structureOff in the CSV compares the course to the *diffused* field, but the layout
            // follows Guide() — tensor where it is confident, contour echo where it is not. Split
            // the same measurement by tensor confidence to see which of the two is being disobeyed.
            {
                var sure = new List<double>();
                var unsure = new List<double>();
                foreach (Tessera[] seq in tesserae.Where(t => t.CourseId >= 0)
                             .GroupBy(t => t.CourseId).Select(g => g.ToArray()))
                {
                    for (int i = 1; i < seq.Length; i++)
                    {
                        double ax = seq[i].Centroid.X - seq[i - 1].Centroid.X;
                        double ay = seq[i].Centroid.Y - seq[i - 1].Centroid.Y;
                        double len = Math.Sqrt((ax * ax) + (ay * ay));
                        if (len < 1e-6) { continue; }
                        double u = seq[i].Centroid.X / layout.FieldWidthMm;
                        double v = seq[i].Centroid.Y / layout.FieldHeightMm;
                        double th = guidance.ThetaAt(u, v);
                        double dot = Math.Abs(((ax / len) * Math.Cos(th)) + ((ay / len) * Math.Sin(th)));
                        double deg = Math.Acos(Math.Clamp(dot, 0.0, 1.0)) * 180.0 / Math.PI;
                        (field.EdgeAt(u, v) > 0.25 ? sure : unsure).Add(deg);
                    }
                }

                static double Med(List<double> xs)
                {
                    if (xs.Count == 0) { return double.NaN; }
                    xs.Sort();
                    return xs[xs.Count / 2];
                }

                Console.WriteLine(
                    $"  угол курса к guidance: на контуре (edge>0,25, {sure.Count * 100.0 / Math.Max(1, sure.Count + unsure.Count):0}% стыков) {Med(sure):0.0}°  " +
                    $"вне контура {Med(unsure):0.0}°");
            }

            // The courses obeying Guide says nothing about Guide being right. Where the tensor is
            // confident, Guide is the tensor by construction; the open question is how much of the
            // panel that covers, and how far the echo takes the rest away from the photograph.
            {
                var sure = new List<double>();
                var unsure = new List<double>();
                for (int gy = 0; gy < field.Height; gy++)
                {
                    for (int gx = 0; gx < field.Width; gx++)
                    {
                        double u = gx / (double)(field.Width - 1);
                        double v = gy / (double)(field.Height - 1);
                        double a = guidance.ThetaAt(u, v);
                        double b = field.ThetaAt(u, v);
                        double deg = Math.Acos(Math.Clamp(
                            Math.Abs((Math.Cos(a) * Math.Cos(b)) + (Math.Sin(a) * Math.Sin(b))),
                            0.0, 1.0)) * 180.0 / Math.PI;
                        (field.CoherenceAt(u, v) >= CourseGuidance.FullConfidence ? sure : unsure).Add(deg);
                    }
                }

                static double Med(List<double> xs)
                {
                    if (xs.Count == 0) { return double.NaN; }
                    xs.Sort();
                    return xs[xs.Count / 2];
                }

                Console.WriteLine(
                    $"  поле ведёт фотография на {sure.Count * 100.0 / (sure.Count + unsure.Count):0}% панно; " +
                    $"эхо уводит остальное от диффузного поля на {Med(unsure):0.0}° (медиана)");

                var coh = new List<double>();
                for (int gy = 0; gy < field.Height; gy++)
                {
                    for (int gx = 0; gx < field.Width; gx++)
                    {
                        coh.Add(field.CoherenceAt(gx / (double)(field.Width - 1), gy / (double)(field.Height - 1)));
                    }
                }

                coh.Sort();
                double C(double q) => coh[Math.Clamp((int)(q * (coh.Count - 1)), 0, coh.Count - 1)];
                Console.WriteLine(
                    $"  когерентность тензора: p50 {C(0.5):0.000}  p75 {C(0.75):0.000}  " +
                    $"p90 {C(0.90):0.000}  p99 {C(0.99):0.000}  max {coh[^1]:0.000}");
            }

            {
                IReadOnlyList<PointD[]> cs = ContourSet.Extract(
                    field, layout.FieldWidthMm, layout.FieldHeightMm, layout.ModuleWidthMm);
                double[] lens = [.. cs.Select(c => Geometry.PolylineLength(c) / layout.ModuleWidthMm).OrderDescending()];
                Console.WriteLine(
                    $"  контуров {cs.Count}" +
                    (lens.Length > 0
                        ? $", длины в модулях: {string.Join(", ", lens.Take(6).Select(l => l.ToString("0")))}"
                          + (lens.Length > 6 ? $" … (ещё {lens.Length - 6})" : string.Empty)
                        : string.Empty));

                // Border rings plus two rows per contour are laid first, so their course ids are the
                // lowest. Everything with a higher id came out of the streamline fill. The border
                // ring count now varies with panel size.
                int borderRings = Tessellation.BorderCourseCount(layout);
                int structural = borderRings + (cs.Count * 2);
                int inStructural = tesserae.Count(t => t.CourseId >= 0 && t.CourseId < structural);
                int inBorder = tesserae.Count(t => t.CourseId >= 0 && t.CourseId < borderRings);
                Console.WriteLine(
                    $"  кусков в бордюре {inBorder * 100.0 / tesserae.Count:0.0}%, " +
                    $"в контурных курсах {(inStructural - inBorder) * 100.0 / tesserae.Count:0.0}%, " +
                    $"в заливке {(tesserae.Count - inStructural) * 100.0 / tesserae.Count:0.0}%");

                var sides = new Dictionary<int, int>();
                foreach (Tessera t in tesserae)
                {
                    sides[t.Polygon.Length] = sides.GetValueOrDefault(t.Polygon.Length) + 1;
                }

                Console.WriteLine("  вершин у куска: " + string.Join("  ",
                    sides.OrderBy(kv => kv.Key)
                         .Select(kv => $"{kv.Key}→{kv.Value * 100.0 / tesserae.Count:0.0}%")));
            }

            CoverageMask mask = CoverageMask.Rasterise(layout, tesserae);
            Metrics.Shape shape = Metrics.Shapes(layout, tesserae, field, guidance);
            // Always measured as the wall will show it — shade plus the joint around it — whatever
            // space the matching itself worked in. Otherwise the two variants are scored by
            // different yardsticks and cannot be compared.
            CieLab[] observed = PaletteObservation.Lab(
                palette, JointOptics.For(layout, palette.TypicalThicknessMm));
            Metrics.Colour colour = Metrics.Colours(
                layout, tesserae, cells, indices, palette, observed, run.MaxColors);
            (double bareR, _) = mask.LargestBare();

            csv.AppendLine(string.Join(',', new[]
            {
                run.Name,
                ColorDistance.MatchingMetric.ToString(),
                string.Create(CultureInfo.InvariantCulture, $"{run.WidthCm:0.#}x{run.HeightCm:0.#}"),
                N(choice.ModuleAlongMm), N(choice.ModuleAcrossMm), N(choice.GroutMm),
                choice.ModulesAcrossShortSide.ToString(CultureInfo.InvariantCulture),
                layout.TotalModules.ToString(CultureInfo.InvariantCulture),
                tesserae.Count.ToString(CultureInfo.InvariantCulture),
                N((double)tesserae.Count / layout.TotalModules),
                N((double)result.CutTesseraCount / tesserae.Count),
                N(mask.OverlappedFraction()), N(mask.CoveredFraction()),
                N(bareR / layout.ModuleHeightMm), N(mask.BareBeyond(layout.ModuleHeightMm * 0.3)),
                N(shape.AreaMin), N(shape.AreaP5), N(shape.AreaMedian), N(shape.AreaP95), N(shape.AreaMax),
                N(shape.TinyShare), N(shape.SliverShare), N(shape.ManySidedShare),
                N(shape.KinkShare),
                shape.CourseCount.ToString(CultureInfo.InvariantCulture),
                N(shape.StubCourseShare), N(shape.MedianCourseLength), N(shape.FillerShare),
                N(shape.MinSideP5), N(shape.UncuttableShare), N(shape.AwkwardShare),
                N(shape.StructureDisagreement), N(shape.EdgesCrossed),
                N(colour.DeltaEMean), N(colour.DeltaEP95), N(colour.DeltaEMax),
                result.ColorsBeforeReduction.ToString(CultureInfo.InvariantCulture),
                colour.ColorsUsed.ToString(CultureInfo.InvariantCulture),
                colour.RareColors.ToString(CultureInfo.InvariantCulture),
                N(colour.DominantShare), N(colour.LightestGap), N(colour.BandingShare), N(colour.MergedShare),
                result.ModulesReassigned.ToString(CultureInfo.InvariantCulture),
                watch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture),
            }));

            Console.WriteLine(
                $"{run.Name,-26} кусок {choice.ModuleAlongMm,2:0}×{choice.ModuleAcrossMm:0}мм {choice.ModulesAcrossShortSide,3} поперёк " +
                $"тессер {tesserae.Count,6} " +
                $"({(double)tesserae.Count / layout.TotalModules:0.00}×)  перекр {mask.OverlappedFraction():P2}  " +
                $"голое>0,3м {mask.BareBeyond(layout.ModuleWidthMm * 0.3):P2}  дыра {bareR / layout.ModuleWidthMm:0.00}м  " +
                $"узкая сторона p5 {shape.MinSideP5:0.0}мм  неколибельных {shape.UncuttableShare:P1}  излом {shape.KinkShare:P1}  " +
                $"ΔE {colour.DeltaEMean:0.0}/{colour.DeltaEP95:0.0}  цветов {colour.ColorsUsed}  " +
                $"обрыв {colour.BandingShare:P1}  слипание {colour.MergedShare:P1}  " +
                $"{watch.ElapsedMilliseconds}мс");
        }

        string csvPath = Path.Combine(outDir, "metrics.csv");
        File.WriteAllText(csvPath, csv.ToString());
        Console.WriteLine($"\n{csvPath}");
        return 0;
    }

    private static string N(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
