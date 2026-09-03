using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Grid;

namespace MosaicGenerator.Core.Quantization;

/// <summary>
/// Settles the sampled colours against their neighbours before anything stretches them.
///
/// A photograph carries texture below the size of a tessera — grain, feather, ripple, the encoder's
/// own noise — and averaging under the outline does not remove all of it. On its own that is
/// harmless: the noise is a fraction of a shade step and every neighbour matches the same article.
/// It stops being harmless the moment the tones are spread, because spreading multiplies whatever
/// is there, and a crowded sky is exactly where the multiplier is largest. Left unsmoothed, the
/// spread turns half a shade of noise into a field of speckle.
///
/// So each cell is pulled toward the neighbours it already agrees with, and left alone where it does
/// not. A boundary — a beak against sky, the edge of a wing — separates cells by far more than the
/// texture does, and those neighbours carry almost no weight, so the boundary survives the pass
/// intact. This is a bilateral filter run over the tesserae themselves rather than over pixels,
/// which is the right scale: nothing below a tessera can be set in glass anyway.
/// </summary>
public static class CellSmoother
{
    public static CieLab[] Settle(
        ReadOnlySpan<CieLab> cells, IReadOnlyList<Tessera> tesserae, MosaicLayout layout)
    {
        ArgumentNullException.ThrowIfNull(tesserae);
        ArgumentNullException.ThrowIfNull(layout);

        if (cells.Length != tesserae.Count || cells.Length == 0)
        {
            return cells.ToArray();
        }

        return Settle(cells, CellNeighbourhood.Build(tesserae, layout));
    }

    /// <summary>Same pass, over a neighbourhood the caller already built.</summary>
    public static CieLab[] Settle(ReadOnlySpan<CieLab> cells, CellNeighbourhood neighbourhood)
    {
        ArgumentNullException.ThrowIfNull(neighbourhood);

        var settled = new CieLab[cells.Length];

        for (int i = 0; i < cells.Length; i++)
        {
            CieLab self = cells[i];
            double sumL = self.L, sumA = self.A, sumB = self.B, weight = 1.0;

            foreach (int j in neighbourhood.Of(i))
            {
                double w = ColorDistance.NeighbourWeight(self, cells[j]);
                sumL += w * cells[j].L;
                sumA += w * cells[j].A;
                sumB += w * cells[j].B;
                weight += w;
            }

            settled[i] = new CieLab(sumL / weight, sumA / weight, sumB / weight);
        }

        return settled;
    }
}
