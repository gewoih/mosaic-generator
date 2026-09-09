using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Imaging;
using MosaicGenerator.Core.Material;
using MosaicGenerator.Core.Pipeline;
using MosaicGenerator.Core.Skia;
using MosaicGenerator.Core.Validation;
using MosaicGenerator.Web.Models;
using MosaicGenerator.Web.Options;
using MosaicGenerator.Web.Services;

namespace MosaicGenerator.Web.Controllers;

[Route("")]
public sealed class MosaicController(
    MosaicGenerationService generator,
    IPaletteRepository palettes,
    IResultStore results,
    ISourceStore sources,
    MosaicGenerationOptions generationOptions,
    IOptions<MosaicOptions> options,
    ILogger<MosaicController> logger) : Controller
{
    private readonly MosaicOptions _options = options.Value;

    [HttpGet("")]
    public IActionResult Index()
    {
        var form = new GenerateFormModel
        {
            PaletteId = palettes.All[0].Id,
            WastePercent = _options.DefaultWastePercent,
        };

        return View(BuildIndexModel(form));
    }

    [HttpPost("generate")]
    [ValidateAntiForgeryToken]
    public IActionResult Generate(GenerateFormModel form)
    {
        ArgumentNullException.ThrowIfNull(form);

        if (form.Photo is { Length: > 0 } photo && photo.Length > _options.MaxUploadBytes)
        {
            ModelState.AddModelError(
                nameof(form.Photo),
                $"Файл больше {_options.MaxUploadMegabytes} МБ.");
        }
        else if (form.Photo is { Length: 0 })
        {
            ModelState.AddModelError(nameof(form.Photo), "Файл пустой.");
        }
        else if (form.Photo is null && string.IsNullOrEmpty(form.SourceId))
        {
            ModelState.AddModelError(nameof(form.Photo), "Выберите фотографию.");
        }

        if (!palettes.TryGet(form.PaletteId, out Palette? palette))
        {
            ModelState.AddModelError(nameof(form.PaletteId), "Такой палитры нет.");
        }

        // The piece's width across its course is the plate's thickness, and the palette is what
        // knows it. Before one is resolved the request is invalid anyway; the fallback only keeps
        // the validation pass from having to special-case that.
        ModuleChoice choice = form.Choose(
            palette?.TypicalThicknessMm ?? ModuleSelector.DefaultPlateThicknessMm);

        foreach (ValidationError error in
                 MosaicRequestValidator.Validate(form.ToRequest(choice), generationOptions.ValidationLimits))
        {
            // The data annotations already cover the plain ranges; adding the core's own wording
            // on top would show the user the same problem twice.
            string field = MapField(error.Field);
            if (!ModelState.TryGetValue(field, out ModelStateEntry? entry) || entry.Errors.Count == 0)
            {
                ModelState.AddModelError(field, error.Message);
            }
        }

        if (!ModelState.IsValid || palette is null)
        {
            return View(nameof(Index), BuildIndexModel(form));
        }

        // A fresh upload is kept so the next pass over the parameters does not need the file
        // again; a regeneration reuses what is already there.
        string? sourceId = form.SourceId;
        if (form.Photo is not null)
        {
            using Stream upload = form.Photo.OpenReadStream();
            sourceId = sources.Save(upload);
        }

        Stream? source = sourceId is null ? null : sources.Open(sourceId);
        if (source is null)
        {
            ModelState.AddModelError(nameof(form.Photo), "Исходник больше не хранится — загрузите фотографию заново.");
            form.SourceId = null;
            return View(nameof(Index), BuildIndexModel(form));
        }

        MosaicResult result;
        try
        {
            using (source)
            {
                result = generator.Generate(source, form.ToRequest(choice), palette);
            }
        }
        catch (InvalidImageException exception)
        {
            ModelState.AddModelError(nameof(form.Photo), exception.Message);
            return View(nameof(Index), BuildIndexModel(form));
        }

        // Guaranteed non-null here: the validation above rejects a request with no upload and no
        // SourceId before this point is reached.
        Dictionary<int, byte[]> ladderCartoons =
            result.ColorLadder.ToDictionary(rung => rung.ColorCount, rung => rung.CartoonPng);

        var recolorState = new RecolorState
        {
            BaseIndices = [.. result.FinalIndices],
            Tesserae = [.. result.Tesserae],
        };

        string id = results.Save(
            Describe(result, form, palette, choice, sourceId!),
            recolorState,
            result.CartoonPng, result.SchemePng, result.LegendPng, ladderCartoons);

        logger.LogInformation(
            "Generated {Columns}x{Rows} pieces of {Along}x{Across} mm from {Palette}, " +
            "{Colors} of {Before} shades kept, as {Id}.",
            result.Layout.Columns,
            result.Layout.Rows,
            choice.ModuleAlongMm,
            choice.ModuleAcrossMm,
            palette.Id,
            result.Report.Lines.Count,
            result.ColorsBeforeReduction,
            id);

        // Post/redirect/get: refreshing the result page must not regenerate.
        return RedirectToAction(nameof(Result), new { id });
    }

    /// <summary>
    /// Same work as <see cref="Generate"/>, posted from the result page with the photograph left
    /// where it is. Settling a panel is a dozen passes over the same picture.
    /// </summary>
    [HttpPost("regenerate")]
    [ValidateAntiForgeryToken]
    public IActionResult Regenerate(GenerateFormModel form) => Generate(form);

    /// <summary>The stored upload, for the crop frame to draw against.</summary>
    [HttpGet("source/{id}")]
    public IActionResult Source(string id)
    {
        using Stream? stream = sources.Open(id);
        if (stream is null)
        {
            return NotFound();
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        byte[] bytes = buffer.ToArray();

        return File(bytes, TempSourceStore.ContentTypeOf(bytes));
    }

    [HttpGet("result/{id}")]
    public IActionResult Result(string id)
    {
        StoredResult? stored = results.Find(id);
        if (stored is null)
        {
            return View("Expired");
        }

        return View(new ResultViewModel
        {
            Id = id,
            Stored = stored,
            Parameters = BuildParameters(FormFrom(stored)),
            CanRecolor = results.FindRecolorState(id) is not null,
        });
    }

    [HttpGet("result/{id}/cartoon.png")]
    public IActionResult Cartoon(string id, int? c = null, bool download = false, string? swaps = null)
    {
        if (Recolored(id, swaps) is { } recolored)
        {
            return download
                ? File(recolored.CartoonPng, "image/png", "karton.png")
                : File(recolored.CartoonPng, "image/png");
        }

        // A shade count the arrows stepped to within the baked range — serve that cartoon straight
        // from the temp folder. Anything else falls through to the one the page was generated at.
        if (c is { } colors)
        {
            byte[]? baked = results.ReadLadderCartoon(id, colors);
            if (baked is not null)
            {
                return download ? File(baked, "image/png", "karton.png") : File(baked, "image/png");
            }
        }

        return Image(id, ResultImage.Cartoon, download, "karton.png");
    }

    [HttpGet("result/{id}/scheme.png")]
    public IActionResult Scheme(string id, bool download = false, string? swaps = null)
    {
        if (Recolored(id, swaps) is { } recolored)
        {
            return download
                ? File(recolored.SchemePng, "image/png", "shema.png")
                : File(recolored.SchemePng, "image/png");
        }

        return Image(id, ResultImage.Scheme, download, "shema.png");
    }

    /// <summary>
    /// The material table after a manual article swap, so the result page can refresh the numbers
    /// without a full round trip. No swaps given — the stored table is already on the page.
    /// </summary>
    [HttpGet("result/{id}/consumption.json")]
    public IActionResult Consumption(string id, string? swaps = null)
    {
        if (Recolored(id, swaps) is not { } recolored)
        {
            return NoContent();
        }

        MaterialReport report = recolored.Report;
        return Json(new
        {
            lines = report.Lines.Select(line => new
            {
                code = line.Code,
                article = line.Color.Article,
                name = line.Color.Name,
                hex = line.Color.Hex,
                count = line.ModuleCount,
                area = line.GrossAreaM2,
                mass = line.MassKg,
                cost = line.Cost,
            }),
            totalArea = report.TotalGrossAreaM2,
            totalMass = report.TotalMassKg,
            totalCost = report.TotalCost,
            totalModules = report.TotalModules,
        });
    }

    /// <summary>
    /// Applies <paramref name="swaps"/> (<c>FROM:TO,FROM:TO</c> by article) to the stored layout
    /// and redraws it. Null, empty, or unusable state — returns null and the caller serves what was
    /// stored.
    /// </summary>
    private RecolorResult? Recolored(string id, string? swaps)
    {
        Dictionary<string, string> map = ParseSwaps(swaps);
        if (map.Count == 0)
        {
            return null;
        }

        StoredResult? stored = results.Find(id);
        RecolorState? state = results.FindRecolorState(id);
        if (stored is null || state is null || !palettes.TryGet(stored.PaletteId, out Palette? palette))
        {
            return null;
        }

        var articleToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < palette.Colors.Count; i++)
        {
            articleToIndex[palette.Colors[i].Article] = i;
        }

        var indexSwaps = new Dictionary<int, int>();
        foreach ((string from, string to) in map)
        {
            if (articleToIndex.TryGetValue(from, out int fromIndex)
                && articleToIndex.TryGetValue(to, out int toIndex)
                && fromIndex != toIndex)
            {
                indexSwaps[fromIndex] = toIndex;
            }
        }

        if (indexSwaps.Count == 0)
        {
            return null;
        }

        var layoutRequest = new MosaicRequest
        {
            PanelWidthMm = stored.PanelWidthMm,
            PanelHeightMm = stored.PanelHeightMm,
            ModuleWidthMm = stored.ModuleSizeMm,
            ModuleHeightMm = stored.ModuleAcrossMm,
            GroutWidthMm = stored.GroutWidthMm,
            PaletteId = stored.PaletteId,
        };

        return generator.Recolor(new RecolorRequest
        {
            Layout = MosaicLayout.Compute(layoutRequest),
            Palette = palette,
            Tesserae = state.Tesserae,
            BaseIndices = state.BaseIndices,
            Swaps = indexSwaps,
            WasteFactor = 1.0 + (stored.WastePercent / 100.0),
            PricePerKgRub = stored.PricePerKgRub,
        });
    }

    private static Dictionary<string, string> ParseSwaps(string? swaps)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(swaps))
        {
            return map;
        }

        foreach (string pair in swaps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = pair.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0)
            {
                map[parts[0]] = parts[1];
            }
        }

        return map;
    }

    /// <summary>
    /// The cartoon laid across A4 pages for 1:1 printing: a panel wider than a sheet is cut, and
    /// the pages carry registration marks and a glue overlap. Same shade-count fallback as
    /// <see cref="Cartoon"/>.
    /// </summary>
    [HttpGet("result/{id}/cartoon.pdf")]
    public IActionResult CartoonPdf(string id, int? c = null, string? swaps = null)
    {
        byte[]? png = Recolored(id, swaps)?.CartoonPng;
        png ??= c is { } colors ? results.ReadLadderCartoon(id, colors) : null;
        png ??= results.ReadImage(id, ResultImage.Cartoon);

        return png is null
            ? NotFound()
            : File(SkiaPdfTiler.Render(png, "Картон"), "application/pdf", "karton-a4.pdf");
    }

    [HttpGet("result/{id}/scheme.pdf")]
    public IActionResult SchemePdf(string id, string? swaps = null)
    {
        byte[]? png = Recolored(id, swaps)?.SchemePng ?? results.ReadImage(id, ResultImage.Scheme);

        return png is null
            ? NotFound()
            : File(SkiaPdfTiler.Render(png, "Схема"), "application/pdf", "shema-a4.pdf");
    }

    [HttpGet("result/{id}/legend.png")]
    public IActionResult Legend(string id, bool download = false, string? swaps = null)
    {
        if (Recolored(id, swaps) is { } recolored)
        {
            return download
                ? File(recolored.LegendPng, "image/png", "legenda.png")
                : File(recolored.LegendPng, "image/png");
        }

        return Image(id, ResultImage.Legend, download, "legenda.png");
    }

    private IActionResult Image(string id, ResultImage image, bool download, string fileName)
    {
        byte[]? bytes = results.ReadImage(id, image);
        if (bytes is null)
        {
            return NotFound();
        }

        return download
            ? File(bytes, "image/png", fileName)
            : File(bytes, "image/png");
    }

    private IndexViewModel BuildIndexModel(GenerateFormModel form) => new()
    {
        Parameters = BuildParameters(form),
        MaxUploadMegabytes = _options.MaxUploadMegabytes,
    };

    private ParametersViewModel BuildParameters(GenerateFormModel form) => new()
    {
        Form = form,
        Palettes = palettes.All,
        LayoutDataJson = LayoutData(),
    };

    /// <summary>
    /// The module range and joint rule as data, so the page can show what a panel size will yield
    /// before anything is uploaded. Serialised from the core's own values: a second copy of the
    /// table in a script would be a second answer waiting to disagree with the first.
    /// </summary>
    private string LayoutData() => JsonSerializer.Serialize(new
    {
        modules = ModuleSelector.AvailableModulesMm
            .Select(module => new { m = module, g = ModuleSelector.GroutFor(module) }),
        maxModules = generationOptions.ValidationLimits.MaxModules,
    });

    private static StoredResult Describe(
        MosaicResult result,
        GenerateFormModel form,
        Palette palette,
        ModuleChoice choice,
        string sourceId) => new()
    {
        PaletteName = palette.Name,
        PaletteId = palette.Id,
        SourceId = sourceId,
        CropAnchorX = form.CropAnchorX,
        CropAnchorY = form.CropAnchorY,
        PinnedArticles = form.PinnedArticles,
        StoppedAtPinnedColors = result.StoppedAtPinnedColors,
        Columns = result.Layout.Columns,
        Rows = result.Layout.Rows,
        PanelWidthMm = result.Layout.PanelWidthMm,
        PanelHeightMm = result.Layout.PanelHeightMm,
        ModuleSizeMm = result.Layout.ModuleWidthMm,
        ModuleAcrossMm = result.Layout.ModuleHeightMm,
        GroutWidthMm = result.Layout.GroutWidthMm,
        MarginXMm = result.Layout.MarginXMm,
        MarginYMm = result.Layout.MarginYMm,
        WastePercent = form.WastePercent,
        PricePerKgRub = form.PricePerKgRub,
        ActualAcross = choice.ModulesAcrossShortSide,
        MaxColors = form.MaxColors,
        AutoColors = result.AutoColors,
        ChosenColors = result.ChosenColors,
        ColorCeiling = result.ColorCeiling,
        ColorLadder = [.. result.ColorLadder.Select(StoredColorRung.From)],
        ColorsBeforeReduction = result.ColorsBeforeReduction,
        ModulesReassigned = result.ModulesReassigned,
        CartoonWidthPx = result.Cartoon.PixelWidth,
        CartoonHeightPx = result.CartoonSheetHeightPx,
        CartoonDpi = result.Cartoon.Dpi,
        SchemeWidthPx = result.Scheme.PixelWidth,
        SchemeHeightPx = result.Scheme.PixelHeight,
        SchemeDpi = result.Scheme.Dpi,
        Lines = [.. result.Report.Lines.Select(StoredMaterialLine.From)],
        TotalGrossAreaM2 = result.Report.TotalGrossAreaM2,
        TotalMassKg = result.Report.TotalMassKg,
        TotalCost = result.Report.TotalCost,
    };

    /// <summary>
    /// The parameters that produced a stored result, so the form on the result page opens set to
    /// them instead of back at the defaults.
    /// </summary>
    private static GenerateFormModel FormFrom(StoredResult stored) => new()
    {
        SourceId = stored.SourceId,
        PanelWidthCm = stored.PanelWidthMm / 10,
        PanelHeightCm = stored.PanelHeightMm / 10,
        ModuleAlongMm = stored.ModuleSizeMm,
        PaletteId = stored.PaletteId,
        MaxColors = stored.MaxColors,
        WastePercent = stored.WastePercent,
        PricePerKgRub = stored.PricePerKgRub,
        CropAnchorX = stored.CropAnchorX,
        CropAnchorY = stored.CropAnchorY,
        PinnedArticles = [.. stored.PinnedArticles],
    };

    /// <summary>
    /// Core field names to form field names, so errors land on the right input. The bite's length
    /// (<c>ModuleWidthMm</c>) is what the mosaicist entered, so a complaint about it belongs on
    /// that field; the width across the course and the joint are derived from the palette rather
    /// than entered, so anything the core rejects about them is really a complaint about the panel.
    /// </summary>
    private static string MapField(string coreField) => coreField switch
    {
        "ModuleWidthMm" => nameof(GenerateFormModel.ModuleAlongMm),
        "ModuleHeightMm" or "GroutWidthMm" or "PanelWidthMm" => nameof(GenerateFormModel.PanelWidthCm),
        "PanelHeightMm" => nameof(GenerateFormModel.PanelHeightCm),
        _ => coreField,
    };
}
