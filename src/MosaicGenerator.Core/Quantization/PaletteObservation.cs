using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Optics;

namespace MosaicGenerator.Core.Quantization;

/// <summary>
/// The palette as the matcher should see it. A shade is chosen to stand for a patch of the
/// photograph, but on the wall that patch is not the shade alone — it is the shade plus the joint
/// around it, and the two mix long before the eye separates them.
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

    /// <summary>
    /// Shades as a finished panel shows them, each mixed with the joint that will surround it.
    ///
    /// The joint is a grey of its own, not a shadow: it pulls light shades down and dark shades up,
    /// so a panel matched without it comes out with less range than the palette had. Matching
    /// against these observed values undoes that — the correction reaches for a lighter article
    /// above the joint's lightness and a darker one below it, and holds the contrast either way.
    /// </summary>
    public static CieLab[] Lab(Palette palette, JointOptics joint)
    {
        ArgumentNullException.ThrowIfNull(palette);

        var lab = new CieLab[palette.Colors.Count];
        for (int i = 0; i < lab.Length; i++)
        {
            lab[i] = joint.Observe(palette.Colors[i].Rgb.ToLinear()).ToLab();
        }

        return lab;
    }
}
