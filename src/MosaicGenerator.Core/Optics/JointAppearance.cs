using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;

namespace MosaicGenerator.Core.Optics;

/// <summary>
/// What the joint between tesserae actually looks like.
///
/// This project assumes smalt bedded in white tile adhesive and left ungrouted, which is how the
/// material is normally laid: the chipped face and the uneven thickness are the point, and grout
/// packed flush would bury both. So the joint is not a colour anyone picks — it is the adhesive
/// bed seen at the bottom of a narrow slot, and very little of the surrounding light reaches down
/// there. White adhesive therefore reads as a dark to mid grey, darker the narrower the joint is
/// relative to how deep it sits.
/// </summary>
public static class JointAppearance
{
    /// <summary>
    /// Cured white tile adhesive: L* ≈ 91. Bright, but well short of paper white — cement never
    /// gets there, and taking it as pure white would overstate every joint by several L*.
    /// </summary>
    public static readonly Rgb AdhesiveWhite = Rgb.FromHex("#E8E6E0");

    /// <summary>
    /// How far the bed sits below the face of the tessera, as a fraction of tessera thickness.
    /// Bedding squeezes adhesive part of the way up the sides, so the open slot is shallower than
    /// the tessera is thick; half is the honest middle of what that leaves.
    /// </summary>
    public const double BedDepthRatio = 0.5;

    /// <summary>
    /// Fraction of the surrounding light reaching the bottom of a long slot of width
    /// <paramref name="slotWidthMm"/> and depth <paramref name="depthMm"/> — the standard
    /// form factor for a groove, <c>sqrt(1 + r²) − r</c> for <c>r = depth / width</c>.
    ///
    /// Light bouncing off the tessera sides back into the slot is ignored, so this is a lower
    /// bound: a real joint sits a little lighter than it predicts, and by more when the sides
    /// are pale.
    /// </summary>
    public static double Occlusion(double slotWidthMm, double depthMm)
    {
        if (slotWidthMm <= 0.0)
        {
            // No slot at all. Nothing of the bed is visible, and the module fraction is 1
            // anyway, so the value never reaches a mix.
            return 0.0;
        }

        if (depthMm <= 0.0)
        {
            return 1.0;
        }

        double ratio = depthMm / slotWidthMm;
        return Math.Sqrt(1.0 + (ratio * ratio)) - ratio;
    }

    /// <summary>Visible bed colour for a joint of this width beside tesserae of this thickness.</summary>
    public static LinearRgb JointColor(double groutWidthMm, double tesseraThicknessMm) =>
        AdhesiveWhite.ToLinear() * Occlusion(groutWidthMm, tesseraThicknessMm * BedDepthRatio);
}

/// <summary>
/// The joint as the eye integrates it: how much of a grid cell is tessera, and what colour fills
/// the rest. At a normal viewing distance the two mix rather than reading separately, so a shade
/// picked to match the photograph on its own comes out too dark once a quarter to a half of its
/// cell turns out to be shaded adhesive.
/// </summary>
public readonly record struct JointOptics
{
    /// <summary>Share of the grid cell covered by the tessera itself.</summary>
    public required double ModuleFraction { get; init; }

    /// <summary>Visible bed colour, already darkened by the slot.</summary>
    public required LinearRgb JointLinear { get; init; }

    public static JointOptics For(
        double moduleWidthMm, double moduleHeightMm, double groutWidthMm, double tesseraThicknessMm)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(moduleWidthMm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(moduleHeightMm);
        ArgumentOutOfRangeException.ThrowIfNegative(groutWidthMm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tesseraThicknessMm);

        double stepX = moduleWidthMm + groutWidthMm;
        double stepY = moduleHeightMm + groutWidthMm;

        return new JointOptics
        {
            ModuleFraction = moduleWidthMm * moduleHeightMm / (stepX * stepY),
            JointLinear = JointAppearance.JointColor(groutWidthMm, tesseraThicknessMm),
        };
    }

    public static JointOptics For(MosaicLayout layout, double tesseraThicknessMm)
    {
        ArgumentNullException.ThrowIfNull(layout);

        return For(layout.ModuleWidthMm, layout.ModuleHeightMm, layout.GroutWidthMm, tesseraThicknessMm);
    }

    /// <summary>The colour this tessera turns into once its own joint is mixed back in.</summary>
    public LinearRgb Observe(LinearRgb tessera) =>
        (tessera * ModuleFraction) + (JointLinear * (1.0 - ModuleFraction));

    /// <summary>Bed colour for painting, as opposed to for mixing.</summary>
    public Rgb JointRgb => JointLinear.ToSrgb().Clamped();
}
