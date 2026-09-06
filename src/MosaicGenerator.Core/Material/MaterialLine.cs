using MosaicGenerator.Core.Domain;

namespace MosaicGenerator.Core.Material;

public sealed record MaterialLine
{
    /// <summary>Short label printed inside the module on the working scheme.</summary>
    public required string Code { get; init; }

    public required PaletteColor Color { get; init; }

    /// <summary>
    /// Articles visually identical to <see cref="Color"/> — what to order instead when it is out
    /// of stock. Empty for a shade that stands alone. See docs/redukciya-svyazka-plan.md (п. 15).
    /// </summary>
    public IReadOnlyList<PaletteColor> Alternatives { get; init; } = [];

    public required int ModuleCount { get; init; }

    /// <summary>Area of the modules themselves.</summary>
    public required double NetAreaM2 { get; init; }

    /// <summary>Area to buy, net area plus the waste allowance.</summary>
    public required double GrossAreaM2 { get; init; }

    public required double MassKg { get; init; }

    public required decimal Cost { get; init; }
}
