using System.Text.Json.Serialization;

namespace MosaicGenerator.Core.Domain;

public sealed class Palette
{
    [JsonConstructor]
    public Palette(string id, string name, IReadOnlyList<PaletteColor> colors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(colors);

        if (colors.Count == 0)
        {
            throw new ArgumentException("A palette needs at least one colour.", nameof(colors));
        }

        Id = id;
        Name = name;
        Colors = colors;

        // Median rather than mean: the joint depth is measured against a representative tessera,
        // and a single odd thickness in the range should not drag it.
        double[] thicknesses = [.. colors.Select(color => color.ThicknessMm).Order()];
        TypicalThicknessMm = thicknesses[thicknesses.Length / 2];
    }

    public string Id { get; }

    public string Name { get; }

    public IReadOnlyList<PaletteColor> Colors { get; }

    /// <summary>
    /// Representative tessera thickness for the range. The joint reads as the adhesive bed seen
    /// down a slot, and how deep that slot is follows from how thick the tesserae beside it are.
    /// </summary>
    [JsonIgnore]
    public double TypicalThicknessMm { get; }
}
