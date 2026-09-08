namespace MosaicGenerator.Core.Domain;

public sealed record MosaicRequest
{
    public required double PanelWidthMm { get; init; }

    public required double PanelHeightMm { get; init; }

    public required double ModuleWidthMm { get; init; }

    public required double ModuleHeightMm { get; init; }

    public required double GroutWidthMm { get; init; }

    public required string PaletteId { get; init; }

    /// <summary>
    /// Where the crop window sits, as a fraction of the source along each axis: 0.5 centres it.
    /// A photograph almost never puts its subject in the middle, and the panel's aspect rarely
    /// matches the camera's, so the centre crop is a default rather than an answer.
    /// </summary>
    public double CropAnchorX { get; init; } = 0.5;

    public double CropAnchorY { get; init; } = 0.5;

    /// <summary>
    /// Articles the colour ceiling must not discard, however few modules they cover. A handful of
    /// tesserae can carry a whole picture — a beak, a catchlight — and counting alone cannot see
    /// that.
    /// </summary>
    public IReadOnlyCollection<string> PinnedArticles { get; init; } = [];

    public double WastePercent { get; init; } = 25.0;

    /// <summary>Average supplier rate for the whole range, in roubles per kilogram.</summary>
    public decimal PricePerKgRub { get; init; } = 1500m;

    /// <summary>
    /// Ceiling on how many shades the finished work may use. Quantising against a large palette
    /// leaves a tail of colours used once or twice, which reads as no detail at all but costs a
    /// separate article each.
    /// </summary>
    public int MaxColors { get; init; } = 20;

    /// <summary>
    /// Overrides the automatic colour pick with an exact shade count, clamped to what the ladder
    /// holds. Zero leaves the choice to <see cref="Quantization.ReductionLadder.Knee"/>: this is
    /// only set when the mosaicist steps the count past the range baked for the on-page arrows and
    /// asks for a full regeneration at that number.
    /// </summary>
    public int ForceColors { get; init; }

    public double WasteFactor => 1.0 + (WastePercent / 100.0);
}
