using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Tests.Support;

namespace MosaicGenerator.Core.Tests.Domain;

public class PaletteClusterTests
{
    [Fact]
    public void DistinctShadesEachFormTheirOwnCluster()
    {
        Palette palette = PaletteFactory.OfHex("#000000", "#808080", "#FFFFFF");

        Assert.Equal(3, palette.Clusters.Count);
        Assert.All(Enumerable.Range(0, 3), i => Assert.Equal(i, palette.RepresentativeOf(i)));
    }

    [Fact]
    public void NearIdenticalShadesShareOneRepresentative()
    {
        // Three whites within ~1 ΔE and one mid grey well away.
        Palette palette = PaletteFactory.OfHex("#FFFFFF", "#FEFEFE", "#FFFEFD", "#808080");

        Assert.Equal(2, palette.Clusters.Count);

        PaletteCluster white = palette.Clusters.Single(c => c.MemberIndices.Count == 3);
        Assert.Equal([0, 1, 2], white.MemberIndices);
        Assert.Contains(white.RepresentativeIndex, white.MemberIndices);
        Assert.All(new[] { 0, 1, 2 }, i => Assert.Equal(white.RepresentativeIndex, palette.RepresentativeOf(i)));
        Assert.Equal(2, white.Alternatives.Count());
    }

    [Fact]
    public void CompleteLinkageDoesNotChainAVisiblyDifferentShadeIn()
    {
        // A ladder in L*: each rung ~2 ΔE from the next, the ends ~8 apart. Single linkage would
        // pull the whole ladder into one cluster; complete linkage must not.
        Palette palette = PaletteFactory.OfHex("#6E6E6E", "#767676", "#7E7E7E", "#868686", "#8E8E8E");

        Assert.True(palette.Clusters.Count >= 3, $"expected the ladder to split, got {palette.Clusters.Count} clusters");
        Assert.All(palette.Clusters, c =>
        {
            foreach (int a in c.MemberIndices)
            {
                foreach (int b in c.MemberIndices)
                {
                    double de = MosaicGenerator.Core.Colors.ColorDistance.CieDe76(
                        palette.Colors[a].Lab, palette.Colors[b].Lab);
                    Assert.True(de < 3.0, $"members {a},{b} are {de:0.0} ΔE apart");
                }
            }
        });
    }

    [Fact]
    public void ClusteringIsDeterministicAcrossRebuilds()
    {
        string[] hexes = ["#FFFFFF", "#FEFEFE", "#FFFEFD", "#101010", "#121212", "#808080"];

        Palette a = PaletteFactory.OfHex(hexes);
        Palette b = PaletteFactory.OfHex(hexes);

        Assert.Equal(
            Enumerable.Range(0, hexes.Length).Select(a.RepresentativeOf),
            Enumerable.Range(0, hexes.Length).Select(b.RepresentativeOf));
    }

    [Fact]
    public void RealArtWorkerPaletteFoldsTheKnownDuplicates()
    {
        var repository = new JsonPaletteRepository("../../../../../src/MosaicGenerator.Web/Data/palettes");
        Assert.True(repository.TryGet("artworker-smalt", out Palette? palette));

        // Measured 2026-09-06: 152 articles, the six whites the largest group. Complete linkage is
        // more conservative than the single-linkage 136; assert the direction, not an exact count.
        Assert.Equal(152, palette!.Colors.Count);
        Assert.InRange(palette.RepresentativeIndices.Count, 136, 151);

        int white = palette.Colors
            .Select((c, i) => (c, i))
            .First(x => x.c.Hex.Equals("#FFFFFF", StringComparison.OrdinalIgnoreCase)).i;
        Assert.True(palette.ClusterOf(white).MemberIndices.Count >= 3);
    }
}
