using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Grid;

namespace MosaicGenerator.Core.Quantization;

/// <summary>
/// Settles the sampled colours against their neighbours before anything stretches them.
///
/// The photograph is already flattened before it is sampled — an ellipse along the form, run over
/// pixels (<see cref="Imaging.ImageFlattener"/>). That removes texture finer than a piece, which is
/// what this pass was originally written for, and for a while it looked as though the two were
/// doing the same work at different scales. They are not, and the difference is where the pass now
/// earns its place: flattening smooths *the picture*, whereas the disagreement this pass removes is
/// created *by the sampling itself*. Nine hundred averages taken under nine hundred outlines land a
/// fraction of a shade apart even off a perfectly smooth photograph, and nothing above the
/// tessellation can see that, because above it the tesserae do not exist yet.
///
/// It stops being harmless the moment the tones are spread, because spreading multiplies whatever
/// is there, and a crowded sky is exactly where the multiplier is largest. So each cell is pulled
/// toward the neighbours it already agrees with, and left alone where it does not. A boundary — a
/// beak against sky, the edge of a wing — separates cells by far more than that residue does, and
/// those neighbours carry almost no weight, so the boundary survives the pass intact.
///
/// Measured 2026-09-05 over 33 runs, ten photographs, four panel sizes, on the flattened signal.
/// Removing the pass costs: singles 0,63 → 0,97 %, pieces in islands of one or two 1,60 → 2,42 %,
/// loud singles 0,41 → 0,60 % — worse in 22 to 25 runs of 33 and better in 3 to 6, which is a
/// direction and not a scatter. On the dolphin at 21×21 the hue drift went 0,000 → 0,028 and the
/// grey back came out with a pink band down it, the defect the acceptance gate is written against.
/// Figures in docs/chetyre-sglazhivatelya-plan.md.
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
