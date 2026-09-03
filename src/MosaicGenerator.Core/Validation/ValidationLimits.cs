namespace MosaicGenerator.Core.Validation;

public sealed record ValidationLimits
{
    public double MinPanelMm { get; init; } = 100;

    public double MaxPanelMm { get; init; } = 3000;

    public double MinModuleMm { get; init; } = 2;

    public double MaxModuleMm { get; init; } = 100;

    public double MaxGroutMm { get; init; } = 20;

    public double MaxWastePercent { get; init; } = 100;

    public decimal MaxPricePerKgRub { get; init; } = 1_000_000;

    public int MinColors { get; init; } = 2;

    public int MaxColors { get; init; } = 100;

    /// <summary>Keeps a synchronous request bounded; 40 000 modules render in well under a second.</summary>
    public int MaxModules { get; init; } = 40_000;

    /// <summary>Beyond this the centre crop would throw away most of the photograph.</summary>
    public double MaxPanelAspect { get; init; } = 5.0;
}
