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

        // Articles too close to tell apart on the cartoon are grouped now, once: the pipeline
        // quantises against one member per group so two indistinguishable articles never share a
        // cartoon, and the legend offers the rest as what to order when the representative is out
        // of stock. See docs/redukciya-svyazka-plan.md (TODO п. 15).
        Clusters = PaletteClustering.Compute(colors);
        RepresentativeIndices = [.. Clusters.Select(c => c.RepresentativeIndex)];

        _clusterOf = new PaletteCluster[colors.Count];
        foreach (PaletteCluster cluster in Clusters)
        {
            foreach (int member in cluster.MemberIndices)
            {
                _clusterOf[member] = cluster;
            }
        }
    }

    private readonly PaletteCluster[] _clusterOf;

    public string Id { get; }

    public string Name { get; }

    public IReadOnlyList<PaletteColor> Colors { get; }

    /// <summary>
    /// Representative tessera thickness for the range. The joint reads as the adhesive bed seen
    /// down a slot, and how deep that slot is follows from how thick the tesserae beside it are.
    /// </summary>
    [JsonIgnore]
    public double TypicalThicknessMm { get; }

    /// <summary>
    /// The articles grouped by visual identity — one representative each, the rest kept as
    /// in-stock alternatives. Every palette index appears in exactly one cluster.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<PaletteCluster> Clusters { get; }

    /// <summary>
    /// The representative index of every cluster, ascending — the shades the pipeline quantises
    /// against.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<int> RepresentativeIndices { get; }

    /// <summary>The representative that stands in for <paramref name="colorIndex"/>'s cluster.</summary>
    public int RepresentativeOf(int colorIndex) => _clusterOf[colorIndex].RepresentativeIndex;

    /// <summary>The cluster <paramref name="colorIndex"/> belongs to.</summary>
    public PaletteCluster ClusterOf(int colorIndex) => _clusterOf[colorIndex];
}
