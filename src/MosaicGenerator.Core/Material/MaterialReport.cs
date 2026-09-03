namespace MosaicGenerator.Core.Material;

public sealed record MaterialReport
{
    public required IReadOnlyList<MaterialLine> Lines { get; init; }

    public required double WasteFactor { get; init; }

    public required decimal PricePerKg { get; init; }

    public int TotalModules => Lines.Sum(line => line.ModuleCount);

    public double TotalNetAreaM2 => Lines.Sum(line => line.NetAreaM2);

    public double TotalGrossAreaM2 => Lines.Sum(line => line.GrossAreaM2);

    public double TotalMassKg => Lines.Sum(line => line.MassKg);

    public decimal TotalCost => Lines.Sum(line => line.Cost);

    public MaterialLine LineFor(int paletteIndex) => Lines[IndexByPalette[paletteIndex]];

    public bool TryGetCode(int paletteIndex, out string code)
    {
        if (IndexByPalette.TryGetValue(paletteIndex, out int line))
        {
            code = Lines[line].Code;
            return true;
        }

        code = string.Empty;
        return false;
    }

    /// <summary>Palette index to position in <see cref="Lines"/>.</summary>
    public required IReadOnlyDictionary<int, int> IndexByPalette { get; init; }
}
