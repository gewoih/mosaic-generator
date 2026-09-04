using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Grid;
using MosaicGenerator.Core.Imaging;
using MosaicGenerator.Core.Material;
using MosaicGenerator.Core.Optics;
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

        SourceImage image = _imageLoader.Load(photo, _options.ImageLimits);
        MosaicLayout layout = MosaicLayout.Compute(request);

        // The crop follows the field, not the panel: the perimeter margin would otherwise stretch
        // the photograph by however much the grid failed to divide evenly.
        CropRect crop = ImageCropper.CropToAspect(
            image.Width, image.Height, layout.FieldAspect, request.CropAnchorX, request.CropAnchorY);

        // Courses that run with the form rather than straight across: the direction field says
        // which way, the tessellation lays the tesserae along it. A featureless photograph leaves
        // the field horizontal, so the layout falls back to a plain staggered grid.
        DirectionField direction = DirectionField.Compute(
            image, crop, layout.FieldAspect, DirectionField.ResolutionFor(layout));
        IReadOnlyList<Tessera> tesserae = Tessellation.Advected(layout, direction);

        LinearRgb[] cells = CellSampler.Sample(image, crop, layout, tesserae);
        CieLab[] cellLab = Quantizer.ToLab(cells);

        // How the joint will read is a consequence of the layout, not a choice: white adhesive
        // seen down a slot whose depth follows from the tessera thickness beside it.
        JointOptics joint = JointOptics.For(layout, palette.TypicalThicknessMm);

        // Matching against the shades as the joint will show them. The joint has a lightness of
        // its own and pulls everything toward it, so matching the bare shades would hand over a
        // panel flatter than the macquette it was approved from.
        CieLab[] paletteLab = request.CompensateJoint
            ? PaletteObservation.Lab(palette, joint)
            : PaletteObservation.Lab(palette);

        // The photograph is laid out in the range the material has before a shade is chosen. Matching
        // the camera's tones directly is accurate and flat: on the gull it put one article over 71 %
        // of the panel because the whole sky sat inside less than one step of the range.
        //
        // Settling first is not optional. Spreading multiplies whatever variation is present, and in
        // a crowded sky that is mostly texture — spread unsettled, it comes out as speckle.
        CellNeighbourhood neighbourhood = CellNeighbourhood.Build(tesserae, layout);
        CieLab[] settledLab = CellSmoother.Settle(cellLab, neighbourhood);

        CieLab[] stretchedLab = ToneMap.IntoPaletteRange(settledLab, paletteLab, request.MaxColors);

        // Spreading the whole picture cannot separate what the picture never separated: haze puts a
        // far ridge and the sky behind it inside one tonal step, and there the cartoon has to lie on
        // purpose, the way a mosaicist does by hand. Last of the three, not before the stretch —
        // the stretch multiplies whatever it is handed, noise included, and that was measured.
        // See docs/lokalnyy-kontrast-plan.md.
        CieLab[] mappedLab = LocalContrast.Lift(
            stretchedLab, CellNeighbourhood.Build(tesserae, LocalContrast.ReachFor(layout)));

        int[] indices = Quantizer.Map(mappedLab, paletteLab);

        // Quantizer picked each cell's nearest shade on its own, with no notion of what the
        // neighbours around it picked. ToneMap's spread is what turns a couple of ΔE of noise into
        // a jump onto a differently saturated article, so this settles the choice against nearby
        // cells before anything downstream treats it as final — see docs/krap-tona-plan.md.
        int[] allColors = [.. Enumerable.Range(0, paletteLab.Length)];
        indices = CoherentMap.Settle(mappedLab, paletteLab, indices, allColors, neighbourhood);

        ReductionOutcome reduction = PaletteReducer.Reduce(
            mappedLab, indices, paletteLab, request.MaxColors,
            PinnedIndices(palette, request.PinnedArticles), tesserae);

        // The reduction re-quantised orphaned cells one at a time as their shade was dropped, so the
        // same disagreement can reappear on the surviving, smaller palette.
        int[] finalIndices = CoherentMap.Settle(
            mappedLab, paletteLab, reduction.Indices, reduction.RetainedColors, neighbourhood);

        var plan = new MosaicPlan(layout, palette, finalIndices, request.EffectiveSeed, tesserae);
        MaterialReport report = MaterialCalculator.Calculate(plan, request.WasteFactor, request.PricePerKgRub);

        RenderPlan cartoon = RenderGeometry.Compute(plan, _options.Cartoon);
        RenderPlan scheme = RenderGeometry.Compute(plan, _options.Scheme);

        return new MosaicResult
        {
            CartoonPng = _renderer.RenderCartoon(cartoon),
            SchemePng = _renderer.RenderScheme(scheme, report),
            Report = report,
            Layout = layout,
            Palette = palette,
            Cartoon = cartoon,
            Scheme = scheme,
            ColorsBeforeReduction = reduction.ColorsBefore,
            ModulesReassigned = reduction.ModulesReassigned,
            StoppedAtPinnedColors = reduction.StoppedAtPinnedColors,
            JointColor = joint.JointRgb,
            JointLightness = joint.JointLinear.ToLab().L,
            ModuleAreaFraction = joint.ModuleFraction,
            TesseraCount = tesserae.Count,
            CutTesseraCount = tesserae.Count(t => t.IsCut),
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
}
