using MosaicGenerator.Core.Material;
using MosaicGenerator.Core.Pipeline;

namespace MosaicGenerator.Web.Services;

/// <summary>Everything the result page needs, minus the images, which are streamed separately.</summary>
public sealed record StoredResult
{
    public required string PaletteName { get; init; }

    public required int Columns { get; init; }

    public required int Rows { get; init; }

    public required double PanelWidthMm { get; init; }

    public required double PanelHeightMm { get; init; }

    public required double ModuleSizeMm { get; init; }

    /// <summary>Width of the piece across its course — the plate's thickness.</summary>
    public required double ModuleAcrossMm { get; init; }

    public required double GroutWidthMm { get; init; }

    /// <summary>The upload this was generated from, so the parameters can be tried again.</summary>
    public required string SourceId { get; init; }

    public required double CropAnchorX { get; init; }

    public required double CropAnchorY { get; init; }

    public required string PaletteId { get; init; }

    public required IReadOnlyList<string> PinnedArticles { get; init; }

    /// <summary>Pins alone exceeded the colour ceiling, so the ceiling gave way.</summary>
    public required bool StoppedAtPinnedColors { get; init; }

    public required double MarginXMm { get; init; }

    public required double MarginYMm { get; init; }

    public required double WastePercent { get; init; }

    public required decimal PricePerKgRub { get; init; }

    /// <summary>Modules that actually fit across the panel's short side, at the chosen bite length.</summary>
    public required int ActualAcross { get; init; }

    public required int MaxColors { get; init; }

    /// <summary>Shades the knee search settled on — the automatic pick, before any manual override.</summary>
    public required int AutoColors { get; init; }

    /// <summary>Shades actually rendered: <see cref="AutoColors"/> unless the count was forced from this page.</summary>
    public required int ChosenColors { get; init; }

    /// <summary>The ceiling the automatic pick was bounded by — the form's «максимум цветов».</summary>
    public required int ColorCeiling { get; init; }

    /// <summary>
    /// Cartoon dimensions and material table per shade count around <see cref="ChosenColors"/>, so
    /// the result page can step through them with no round trip. The cartoons themselves are stored
    /// as <c>cartoon-c{n}.png</c> beside the manifest.
    /// </summary>
    public required IReadOnlyList<StoredColorRung> ColorLadder { get; init; }

    public required int ColorsBeforeReduction { get; init; }

    public required int ModulesReassigned { get; init; }

    public required int CartoonWidthPx { get; init; }

    public required int CartoonHeightPx { get; init; }

    public required int SchemeWidthPx { get; init; }

    public required int SchemeHeightPx { get; init; }

    public required double CartoonDpi { get; init; }

    public required double SchemeDpi { get; init; }

    public required IReadOnlyList<StoredMaterialLine> Lines { get; init; }

    public required double TotalGrossAreaM2 { get; init; }

    public required double TotalMassKg { get; init; }

    public required decimal TotalCost { get; init; }

    public int TotalModules => Columns * Rows;
}

/// <summary>One step of the colour ladder as the result page needs it, minus the cartoon PNG.</summary>
public sealed record StoredColorRung
{
    public required int ColorCount { get; init; }

    public required int CartoonHeightPx { get; init; }

    public required int ModulesReassigned { get; init; }

    public required IReadOnlyList<StoredMaterialLine> Lines { get; init; }

    public required double TotalGrossAreaM2 { get; init; }

    public required double TotalMassKg { get; init; }

    public required decimal TotalCost { get; init; }

    public static StoredColorRung From(ColorLadderRung rung) => new()
    {
        ColorCount = rung.ColorCount,
        CartoonHeightPx = rung.CartoonSheetHeightPx,
        ModulesReassigned = rung.ModulesReassigned,
        Lines = [.. rung.Report.Lines.Select(StoredMaterialLine.From)],
        TotalGrossAreaM2 = rung.Report.TotalGrossAreaM2,
        TotalMassKg = rung.Report.TotalMassKg,
        TotalCost = rung.Report.TotalCost,
    };
}

public sealed record StoredMaterialLine
{
    public required string Code { get; init; }

    public required string Article { get; init; }

    public required string Name { get; init; }

    public required string Hex { get; init; }

    public required int ModuleCount { get; init; }

    public required double GrossAreaM2 { get; init; }

    public required double MassKg { get; init; }

    public required decimal Cost { get; init; }

    public static StoredMaterialLine From(MaterialLine line) => new()
    {
        Code = line.Code,
        Article = line.Color.Article,
        Name = line.Color.Name,
        Hex = line.Color.Hex,
        ModuleCount = line.ModuleCount,
        GrossAreaM2 = line.GrossAreaM2,
        MassKg = line.MassKg,
        Cost = line.Cost,
    };
}
