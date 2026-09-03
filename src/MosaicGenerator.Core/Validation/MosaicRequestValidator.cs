using System.Globalization;
using MosaicGenerator.Core.Domain;

namespace MosaicGenerator.Core.Validation;

public static class MosaicRequestValidator
{
    public static IReadOnlyList<ValidationError> Validate(MosaicRequest request, ValidationLimits limits)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(limits);

        var errors = new List<ValidationError>();

        CheckRange(errors, nameof(request.PanelWidthMm), "Ширина панно",
            request.PanelWidthMm, limits.MinPanelMm, limits.MaxPanelMm);
        CheckRange(errors, nameof(request.PanelHeightMm), "Высота панно",
            request.PanelHeightMm, limits.MinPanelMm, limits.MaxPanelMm);
        CheckRange(errors, nameof(request.ModuleWidthMm), "Размер модуля",
            request.ModuleWidthMm, limits.MinModuleMm, limits.MaxModuleMm);
        CheckRange(errors, nameof(request.ModuleHeightMm), "Размер модуля",
            request.ModuleHeightMm, limits.MinModuleMm, limits.MaxModuleMm);
        CheckRange(errors, nameof(request.GroutWidthMm), "Ширина шва",
            request.GroutWidthMm, 0, limits.MaxGroutMm);
        CheckRange(errors, nameof(request.WastePercent), "Запас на отходы",
            request.WastePercent, 0, limits.MaxWastePercent);
        CheckRange(errors, nameof(request.PricePerKgRub), "Цена материала",
            (double)request.PricePerKgRub, 0, (double)limits.MaxPricePerKgRub);
        CheckRange(errors, nameof(request.MaxColors), "Максимум цветов",
            request.MaxColors, limits.MinColors, limits.MaxColors);
        CheckRange(errors, nameof(request.CropAnchorX), "Кадрирование по горизонтали",
            request.CropAnchorX, 0, 1);
        CheckRange(errors, nameof(request.CropAnchorY), "Кадрирование по вертикали",
            request.CropAnchorY, 0, 1);

        if (string.IsNullOrWhiteSpace(request.PaletteId))
        {
            errors.Add(new ValidationError(nameof(request.PaletteId), "Не выбрана палитра."));
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        if (request.ModuleWidthMm > request.PanelWidthMm || request.ModuleHeightMm > request.PanelHeightMm)
        {
            errors.Add(new ValidationError(
                nameof(request.ModuleWidthMm), "Модуль больше панно — ни один модуль не поместится."));
            return errors;
        }

        double aspect = request.PanelWidthMm / request.PanelHeightMm;
        if (aspect > limits.MaxPanelAspect || aspect < 1.0 / limits.MaxPanelAspect)
        {
            errors.Add(new ValidationError(
                nameof(request.PanelWidthMm),
                $"Слишком вытянутое панно: соотношение сторон должно быть в пределах " +
                $"1:{Format(limits.MaxPanelAspect)}…{Format(limits.MaxPanelAspect)}:1."));
        }

        int columns = MosaicLayout.FitCount(request.PanelWidthMm, request.ModuleWidthMm, request.GroutWidthMm);
        int rows = MosaicLayout.FitCount(request.PanelHeightMm, request.ModuleHeightMm, request.GroutWidthMm);
        long modules = (long)columns * rows;

        if (modules > limits.MaxModules)
        {
            errors.Add(new ValidationError(
                nameof(request.ModuleWidthMm),
                $"При таких размерах получается {modules:N0} модулей, максимум {limits.MaxModules:N0}. " +
                "Увеличьте модуль или уменьшите панно."));
        }

        return errors;
    }

    private static void CheckRange(
        List<ValidationError> errors, string field, string label, double value, double min, double max)
    {
        if (double.IsNaN(value) || value < min || value > max)
        {
            errors.Add(new ValidationError(field, $"{label}: допустимо от {Format(min)} до {Format(max)}."));
        }
    }

    private static string Format(double value) =>
        value.ToString(value == Math.Floor(value) ? "0" : "0.##", CultureInfo.InvariantCulture);
}
