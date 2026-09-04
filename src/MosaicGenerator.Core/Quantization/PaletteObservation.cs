using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;

namespace MosaicGenerator.Core.Quantization;

/// <summary>
/// The palette as the matcher sees it: the shades as they are in the hand.
///
/// There was once a second reading here, each shade mixed with the joint that would surround it,
/// and the match ran in that space. It was measured over 48 runs and removed — see
/// docs/tsvetnoy-obodok-plan.md.
/// </summary>
public static class PaletteObservation
{
    /// <summary>Shades as they are in the hand.</summary>
    public static CieLab[] Lab(Palette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);

        var lab = new CieLab[palette.Colors.Count];
        for (int i = 0; i < lab.Length; i++)
        {
            lab[i] = palette.Colors[i].Lab;
        }

        return lab;
    }
}
