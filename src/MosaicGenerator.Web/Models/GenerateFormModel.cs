using System.ComponentModel.DataAnnotations;
using MosaicGenerator.Core.Domain;

namespace MosaicGenerator.Web.Models;

/// <summary>
/// The form as posted. The joint is not asked for: it is derived from the plate thickness of the
/// chosen palette. The module width across its course is derived the same way; only its length
/// along the course — the mosaicist's chosen bite, a floor rather than a fixed size — is entered.
/// The ranges here mirror the core validator so the browser can reject the obvious cases; the core
/// validator remains the authority and runs on every submission.
/// </summary>
public sealed class GenerateFormModel
{
    [Display(Name = "Фотография")]
    public IFormFile? Photo { get; set; }

    /// <summary>
    /// Photograph already on the server from an earlier pass. Set on every regeneration, so
    /// settling a panel does not mean re-picking the same file a dozen times.
    /// </summary>
    public string? SourceId { get; set; }

    [Display(Name = "Ширина панно, см")]
    [Range(10, 300, ErrorMessage = "Ширина панно: от 10 до 300 см.")]
    public double PanelWidthCm { get; set; } = 15;

    [Display(Name = "Высота панно, см")]
    [Range(10, 300, ErrorMessage = "Высота панно: от 10 до 300 см.")]
    public double PanelHeightCm { get; set; } = 15;

    [Display(Name = "Минимальная длина откуса, мм")]
    public double ModuleAlongMm { get; set; } = 6;

    /// <summary>
    /// Which part of the photograph the panel keeps, as a fraction along each axis. The subject
    /// is rarely in the middle and the panel's proportions rarely match the camera's, so this is
    /// a decision rather than a default.
    /// </summary>
    [Display(Name = "Кадрирование по горизонтали")]
    [Range(0, 1, ErrorMessage = "Кадрирование: доля от 0 до 1.")]
    public double CropAnchorX { get; set; } = 0.5;

    [Display(Name = "Кадрирование по вертикали")]
    [Range(0, 1, ErrorMessage = "Кадрирование: доля от 0 до 1.")]
    public double CropAnchorY { get; set; } = 0.5;

    /// <summary>Articles the colour ceiling must leave alone, chosen from a previous result.</summary>
    [Display(Name = "Не сворачивать артикулы")]
    public string[] PinnedArticles { get; set; } = [];

    [Display(Name = "Палитра")]
    [Required(ErrorMessage = "Выберите палитру.")]
    public string PaletteId { get; set; } = string.Empty;

    [Display(Name = "Максимум цветов")]
    [Range(2, 100, ErrorMessage = "Максимум цветов: от 2 до 100.")]
    public int MaxColors { get; set; } = 8;

    [Display(Name = "Запас на отходы, %")]
    [Range(0, 100, ErrorMessage = "Запас на отходы: от 0 до 100 %.")]
    public double WastePercent { get; set; } = 25;

    [Display(Name = "Цена материала, ₽/кг")]
    [Range(0, 1_000_000, ErrorMessage = "Цена материала: от 0 до 1 000 000 ₽/кг.")]
    public decimal PricePerKgRub { get; set; } = 1500;

    public double PanelWidthMm => PanelWidthCm * 10;

    public double PanelHeightMm => PanelHeightCm * 10;

    /// <summary>
    /// Tessera and joint for this panel. The piece's width across its course is the plate's
    /// thickness, so the palette has to be asked: it is the palette that says how thick the
    /// material is.
    /// </summary>
    public ModuleChoice Choose(double plateThicknessMm) =>
        ModuleSelector.Choose(PanelWidthMm, PanelHeightMm, ModuleAlongMm, plateThicknessMm);

    public MosaicRequest ToRequest(ModuleChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);

        return new MosaicRequest
        {
            PanelWidthMm = PanelWidthMm,
            PanelHeightMm = PanelHeightMm,
            ModuleWidthMm = choice.ModuleAlongMm,
            ModuleHeightMm = choice.ModuleAcrossMm,
            GroutWidthMm = choice.GroutMm,
            CropAnchorX = CropAnchorX,
            CropAnchorY = CropAnchorY,
            PinnedArticles = PinnedArticles,
            PaletteId = PaletteId,
            MaxColors = MaxColors,
            WastePercent = WastePercent,
            PricePerKgRub = PricePerKgRub,
        };
    }
}
