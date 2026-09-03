using MosaicGenerator.Core.Material;

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

    /// <summary>What the joint reads as once the slot has taken its light, for display.</summary>
    public required string JointHex { get; init; }

    /// <summary>Its lightness in L*, against roughly 91 for the adhesive out in the open.</summary>
    public required double JointLightness { get; init; }

    /// <summary>Share of the panel that is tessera rather than joint.</summary>
    public required double ModuleAreaFraction { get; init; }

    /// <summary>Whether shade matching accounted for that joint.</summary>
    public required bool CompensateJoint { get; init; }

    /// <summary>The upload this was generated from, so the parameters can be tried again.</summary>
    public required string SourceId { get; init; }

    public required double CropAnchorX { get; init; }

    public required double CropAnchorY { get; init; }

    /// <summary>Detail level as posted, so the form on the result page comes back set to it.</summary>
    public required string DetailLevelValue { get; init; }

    public required string PaletteId { get; init; }

    public required IReadOnlyList<string> PinnedArticles { get; init; }

    /// <summary>Pins alone exceeded the colour ceiling, so the ceiling gave way.</summary>
    public required bool StoppedAtPinnedColors { get; init; }

    public required double MarginXMm { get; init; }

    public required double MarginYMm { get; init; }

    public required double WastePercent { get; init; }

    public required decimal PricePerKgRub { get; init; }

    public required string DetailLevel { get; init; }

    public required int RequestedAcross { get; init; }

    public required int ActualAcross { get; init; }

    /// <summary>Empty when the requested detail level was reached.</summary>
    public required string LimitNote { get; init; }

    public required int MaxColors { get; init; }

    public required int ColorsBeforeReduction { get; init; }

    public required int ModulesReassigned { get; init; }

    public required int PreviewWidthPx { get; init; }

    public required int PreviewHeightPx { get; init; }

    public required int SchemeWidthPx { get; init; }

    public required int SchemeHeightPx { get; init; }

    public required double PreviewDpi { get; init; }

    public required double SchemeDpi { get; init; }

    public required IReadOnlyList<StoredMaterialLine> Lines { get; init; }

    public required double TotalGrossAreaM2 { get; init; }

    public required double TotalMassKg { get; init; }

    public required decimal TotalCost { get; init; }

    public int TotalModules => Columns * Rows;
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
