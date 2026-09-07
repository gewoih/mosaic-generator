using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Grid;
using MosaicGenerator.Core.Imaging;
using MosaicGenerator.Core.Material;
using MosaicGenerator.Core.Quantization;
using MosaicGenerator.Core.Rendering;
using MosaicGenerator.Core.Validation;

namespace MosaicGenerator.Core.Pipeline;

/// <summary>
/// Photograph in, macquette out: crop, sample, quantise, cost, render. Lives in the core rather
/// than the web project so the whole chain can be exercised without an HTTP request.
/// </summary>
public sealed class MosaicGenerationService(
    IImageLoader imageLoader,
    IMosaicRenderer renderer,
    MosaicGenerationOptions options)
{
    private readonly IImageLoader _imageLoader = imageLoader ?? throw new ArgumentNullException(nameof(imageLoader));
    private readonly IMosaicRenderer _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    private readonly MosaicGenerationOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>How far either side of the automatic pick the on-page arrows can step without a round trip.</summary>
    private const int LadderReach = 5;

    public MosaicResult Generate(Stream photo, MosaicRequest request, Palette palette)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(palette);

        IReadOnlyList<ValidationError> errors =
            MosaicRequestValidator.Validate(request, _options.ValidationLimits);
        if (errors.Count > 0)
        {
            throw new ArgumentException(
                $"Invalid request: {string.Join("; ", errors.Select(e => e.Message))}", nameof(request));
        }

        Prepared context = Prepare(photo, request, palette);

        // The ladder is built down past the ceiling so the knee search has cheap rungs to measure a
        // median against; 4 is the floor the pick itself will never go below.
        int floor = Math.Clamp(Math.Min(4, request.MaxColors), 1, context.PaletteLab.Length);
        ReductionLadder ladder = PaletteReducer.BuildLadder(
            context.MappedLab, context.Indices, context.PaletteLab, floor,
            context.Pinned, context.Tesserae, context.Neighbourhood);

        ColorLadderPick pick = ladder.Knee(request.MaxColors);

        int chosen = request.ForceColors > 0
            ? Math.Clamp(request.ForceColors, ladder.Floor.ColorCount, ladder.Rungs[0].ColorCount)
            : pick.Colors;

        Rendered main = RenderAt(context, ladder.RungAt(chosen), full: true);

        var baked = new List<ColorLadderRung>();
        if (_options.BakeColorLadder)
        {
            int top = ladder.Rungs[0].ColorCount;
            int bottom = ladder.Floor.ColorCount;
            int from = Math.Max(Math.Max(bottom, 2), chosen - LadderReach);
            int to = Math.Min(Math.Min(top, request.MaxColors), chosen + LadderReach);

            for (int n = from; n <= to; n++)
            {
                ReductionRung rung = ladder.RungAt(n);
                Rendered rendered = n == chosen ? main : RenderAt(context, rung, full: false);
                baked.Add(new ColorLadderRung
                {
                    ColorCount = n,
                    CartoonPng = rendered.CartoonPng,
                    CartoonSheetHeightPx = rendered.CartoonSheetHeightPx,
                    Report = rendered.Report,
                    ModulesReassigned = rung.ModulesReassigned,
                });
            }
        }

        ReductionRung chosenRung = ladder.RungAt(chosen);

        return new MosaicResult
        {
            CartoonPng = main.CartoonPng,
            SchemePng = main.SchemePng!,
            LegendPng = main.LegendPng!,
            CartoonSheetHeightPx = main.CartoonSheetHeightPx,
            Report = main.Report,
            Layout = context.Layout,
            Palette = palette,
            Cartoon = main.Cartoon,
            Scheme = main.Scheme!,
            ColorsBeforeReduction = ladder.ColorsBefore,
            AutoColors = pick.Colors,
            ChosenColors = chosen,
            ColorCeiling = request.MaxColors,
            KneeRatio = pick.KneeRatio,
            ColorLadder = baked,
            ModulesReassigned = chosenRung.ModulesReassigned,
            SettledAfterReduction = main.SettledAfterReduction,
            SettledAfterReductionOnMoved = main.SettledAfterReductionOnMoved,
            StoppedAtPinnedColors = ladder.StoppedAtPinnedColors,
            TesseraCount = context.Tesserae.Count,
            CutTesseraCount = context.Tesserae.Count(t => t.IsCut),
        };
    }

    /// <summary>
    /// Everything the layout needs before a shade count is chosen: crop, direction field,
    /// tessellation, sampling, the tonal passes, the first quantisation and the first
    /// <see cref="CoherentMap"/>. None of it reads <see cref="MosaicRequest.MaxColors"/> or the
    /// pins, and <see cref="MosaicRequest.EffectiveSeed"/> does not hash them either — so the
    /// prefix is computed once and every rung of the colour ladder shares it.
    /// </summary>
    private Prepared Prepare(Stream photo, MosaicRequest request, Palette palette)
    {
        SourceImage image = _imageLoader.Load(photo, _options.ImageLimits);
        MosaicLayout layout = MosaicLayout.Compute(request);

        // The crop follows the field, not the panel: the perimeter margin would otherwise stretch
        // the photograph by however much the grid failed to divide evenly.
        CropRect crop = ImageCropper.CropToAspect(
            image.Width, image.Height, layout.FieldAspect, request.CropAnchorX, request.CropAnchorY);

        // Flatten the photograph into plateaus at tessera scale before anything reads it. Texture
        // finer than a piece cannot be laid; averaged under a tessera it becomes noise, and ToneMap
        // then multiplies that noise into crumb. The kernel is an ellipse laid along the form, so
        // what it spends is detail no course could show. See docs/ploskosti-spike.md.
        if (_options.Flatten is { } flatten)
        {
            image = ImageFlattener.Apply(
                image, layout.ModuleWidthMm / layout.FieldWidthMm * crop.Width, flatten);
        }

        // Courses that run with the form rather than straight across: the direction field says
        // which way, the tessellation lays the tesserae along it. A featureless photograph leaves
        // the field horizontal, so the layout falls back to a plain staggered grid.
        DirectionField direction = DirectionField.Compute(
            image, crop, layout.FieldAspect, DirectionField.ResolutionFor(layout));
        IReadOnlyList<Tessera> tesserae = Tessellation.Advected(layout, direction);

        LinearRgb[] cells = CellSampler.Sample(image, crop, layout, tesserae);
        CieLab[] cellLab = Quantizer.ToLab(cells);

        // Matching against the shades as they are in the hand, not as the joint will show them.
        // See docs/tsvetnoy-obodok-plan.md for why the joint compensation was measured and dropped.
        CieLab[] paletteLab = PaletteObservation.Lab(palette);

        // The photograph is laid out in the range the material has before a shade is chosen.
        // Settling first is not optional: spreading multiplies whatever variation is present, and
        // in a crowded sky that is mostly texture.
        CellNeighbourhood neighbourhood = CellNeighbourhood.Build(tesserae, layout);
        CieLab[] settledLab = CellSmoother.Settle(cellLab, neighbourhood);

        // The tonal step ToneMap fades out below is taken from the palette's own tonal density, not
        // from request.MaxColors — see docs/14-maxcolors-dve-veshchi-plan.md (TODO п. 14).
        CieLab[] stretchedLab = ToneMap.IntoPaletteRange(settledLab, paletteLab);

        // Spreading the whole picture cannot separate what the picture never separated. Last of the
        // three, not before the stretch. See docs/lokalnyy-kontrast-plan.md.
        CieLab[] mappedLab = LocalContrast.Lift(
            stretchedLab, CellNeighbourhood.Build(tesserae, LocalContrast.ReachFor(layout)));

        // Against the cluster representatives, not the whole palette — see
        // docs/redukciya-svyazka-plan.md (TODO п. 15).
        IReadOnlyList<int> candidateColors = palette.RepresentativeIndices;
        int[] indices = Quantizer.Map(mappedLab, paletteLab, candidateColors);

        // Quantizer picked each cell's nearest shade on its own; this settles the choice against
        // nearby cells before anything downstream treats it as final — see docs/krap-tona-plan.md.
        indices = CoherentMap.Settle(mappedLab, paletteLab, indices, candidateColors, neighbourhood);

        return new Prepared
        {
            Request = request,
            Palette = palette,
            Layout = layout,
            Tesserae = tesserae,
            Neighbourhood = neighbourhood,
            MappedLab = mappedLab,
            PaletteLab = paletteLab,
            Indices = indices,
            Pinned = PinnedIndices(palette, request.PinnedArticles),
        };
    }

    /// <summary>
    /// The tail from one rung of the ladder: a settling pass against the reduced palette, then the
    /// material report and the renders. Only the chosen rung asks for the numbered scheme and the
    /// legend — those carry per-shade numbers a stepped count would invalidate.
    /// </summary>
    private Rendered RenderAt(Prepared context, ReductionRung rung, bool full)
    {
        // A settling pass against the reduced palette — the only place the layout is agreed against
        // the final set. Measured over 33 runs (docs/redukciya-svyazka-plan.md §11).
        int[] finalIndices = CoherentMap.Settle(
            context.MappedLab, context.PaletteLab, rung.Indices, rung.RetainedColors, context.Neighbourhood);

        // Diagnostic only — the sentry on TODO п. 13: a piece this pass moves that the reducer
        // never touched was never an orphan, so its disagreement is not the hand-out's fault.
        int settledAfterReduction = 0;
        int settledAfterReductionOnMoved = 0;
        for (int cell = 0; cell < finalIndices.Length; cell++)
        {
            if (finalIndices[cell] == rung.Indices[cell])
            {
                continue;
            }

            settledAfterReduction++;
            if (rung.Indices[cell] != context.Indices[cell])
            {
                settledAfterReductionOnMoved++;
            }
        }

        var plan = new MosaicPlan(
            context.Layout, context.Palette, finalIndices, context.Request.EffectiveSeed, context.Tesserae);
        MaterialReport report = MaterialCalculator.Calculate(
            plan, context.Request.WasteFactor, context.Request.PricePerKgRub);

        RenderPlan cartoonGeometry = RenderGeometry.Compute(plan, _options.Cartoon);
        byte[] cartoonPng = _renderer.RenderCartoon(cartoonGeometry);
        int sheetHeight = CartoonSheet.Layout(cartoonGeometry).HeightPx;

        if (!full)
        {
            return new Rendered
            {
                CartoonPng = cartoonPng,
                CartoonSheetHeightPx = sheetHeight,
                Cartoon = cartoonGeometry,
                Report = report,
                SettledAfterReduction = settledAfterReduction,
                SettledAfterReductionOnMoved = settledAfterReductionOnMoved,
            };
        }

        RenderPlan schemeGeometry = RenderGeometry.Compute(plan, _options.Scheme);

        return new Rendered
        {
            CartoonPng = cartoonPng,
            CartoonSheetHeightPx = sheetHeight,
            Cartoon = cartoonGeometry,
            Report = report,
            Scheme = schemeGeometry,
            SchemePng = _renderer.RenderScheme(schemeGeometry, report),
            LegendPng = _renderer.RenderLegend(report),
            SettledAfterReduction = settledAfterReduction,
            SettledAfterReductionOnMoved = settledAfterReductionOnMoved,
        };
    }

    /// <summary>
    /// Articles the reduction may not discard, as palette indices. Unknown articles are ignored
    /// rather than rejected: a pin outlives the palette it was made against.
    /// </summary>
    private static IReadOnlySet<int>? PinnedIndices(Palette palette, IReadOnlyCollection<string> articles)
    {
        if (articles.Count == 0)
        {
            return null;
        }

        var wanted = new HashSet<string>(articles, StringComparer.OrdinalIgnoreCase);
        var indices = new HashSet<int>();

        for (int i = 0; i < palette.Colors.Count; i++)
        {
            if (wanted.Contains(palette.Colors[i].Article))
            {
                indices.Add(i);
            }
        }

        return indices.Count > 0 ? indices : null;
    }

    /// <summary>The pipeline prefix, computed once and shared by every rung of the colour ladder.</summary>
    private sealed record Prepared
    {
        public required MosaicRequest Request { get; init; }

        public required Palette Palette { get; init; }

        public required MosaicLayout Layout { get; init; }

        public required IReadOnlyList<Tessera> Tesserae { get; init; }

        public required CellNeighbourhood Neighbourhood { get; init; }

        public required CieLab[] MappedLab { get; init; }

        public required CieLab[] PaletteLab { get; init; }

        /// <summary>The mapping after the first <see cref="CoherentMap"/> pass, before any reduction.</summary>
        public required int[] Indices { get; init; }

        public required IReadOnlySet<int>? Pinned { get; init; }
    }

    /// <summary>The output of <see cref="RenderAt"/>. Scheme and legend are set only for the chosen rung.</summary>
    private sealed record Rendered
    {
        public required byte[] CartoonPng { get; init; }

        public required int CartoonSheetHeightPx { get; init; }

        public required RenderPlan Cartoon { get; init; }

        public required MaterialReport Report { get; init; }

        public RenderPlan? Scheme { get; init; }

        public byte[]? SchemePng { get; init; }

        public byte[]? LegendPng { get; init; }

        public required int SettledAfterReduction { get; init; }

        public required int SettledAfterReductionOnMoved { get; init; }
    }
}
