namespace MosaicGenerator.Core.Domain;

/// <summary>
/// A group of palette articles too close in colour to tell apart on the cartoon or the monitor.
/// The pipeline quantises against one member — <see cref="RepresentativeIndex"/> — so two
/// indistinguishable articles can never land in the same cartoon under different numbers; the
/// others stay in the catalogue as <see cref="MemberIndices"/> and are shown in the legend as
/// what to order when the representative is out of stock. See <c>docs/redukciya-svyazka-plan.md</c>
/// (TODO п. 15).
/// </summary>
public sealed class PaletteCluster
{
    public PaletteCluster(int representativeIndex, IReadOnlyList<int> memberIndices)
    {
        ArgumentNullException.ThrowIfNull(memberIndices);
        if (memberIndices.Count == 0)
        {
            throw new ArgumentException("A cluster needs at least one member.", nameof(memberIndices));
        }

        if (!memberIndices.Contains(representativeIndex))
        {
            throw new ArgumentException(
                "The representative must be one of the members.", nameof(representativeIndex));
        }

        RepresentativeIndex = representativeIndex;
        MemberIndices = memberIndices;
    }

    /// <summary>Palette index the pipeline quantises against for this cluster.</summary>
    public int RepresentativeIndex { get; }

    /// <summary>Every palette index in the cluster, the representative included, ascending.</summary>
    public IReadOnlyList<int> MemberIndices { get; }

    /// <summary>The members other than the representative — the in-stock alternatives.</summary>
    public IEnumerable<int> Alternatives => MemberIndices.Where(i => i != RepresentativeIndex);
}
