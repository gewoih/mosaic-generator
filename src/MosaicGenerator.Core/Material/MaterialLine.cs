using MosaicGenerator.Core.Domain;

namespace MosaicGenerator.Core.Material;

public sealed record MaterialLine
{
    /// <summary>Short label printed inside the module on the working scheme.</summary>
    public required string Code { get; init; }

    public required PaletteColor Color { get; init; }

    public required int ModuleCount { get; init; }

    /// <summary>Area of the modules themselves.</summary>
    public required double NetAreaM2 { get; init; }

    /// <summary>Area to buy, net area plus the waste allowance.</summary>
    public required double GrossAreaM2 { get; init; }

    public required double MassKg { get; init; }

    public required decimal Cost { get; init; }
}
