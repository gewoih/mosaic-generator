namespace MosaicGenerator.Core.Colors;

public static class ColorDistance
{
    /// <summary>
    /// ΔE at which two neighbouring cells stop counting as the same field, for the Gaussian
    /// weight <see cref="NeighbourWeight"/> uses. Texture between neighbouring tesserae — grain,
    /// feather, ripple — runs a couple of ΔE; a real boundary runs ten or more. Five sits between
    /// the two, so noise is averaged away and edges are not. Shared by <c>CellSmoother</c> and
    /// <c>CoherentMap</c>, which both need the same notion of "still the same field".
    /// </summary>
    public const double NeighbourFalloff = 5.0;

    /// <summary>
    /// How much a neighbour counts, from how close its photograph colour is to this cell's own —
    /// 1 for an identical neighbour, fading to 0 past <see cref="NeighbourFalloff"/> ΔE.
    /// </summary>
    public static double NeighbourWeight(CieLab a, CieLab b) =>
        Math.Exp(-CieDe76Squared(a, b) / (2.0 * NeighbourFalloff * NeighbourFalloff));

    /// <summary>
    /// Squared CIE76 difference. Monotonic in CIE76, so it orders candidates identically
    /// while avoiding a square root per palette entry in the quantizer's inner loop.
    /// </summary>
    public static double CieDe76Squared(CieLab a, CieLab b)
    {
        double dl = a.L - b.L;
        double da = a.A - b.A;
        double db = a.B - b.B;
        return (dl * dl) + (da * da) + (db * db);
    }

    public static double CieDe76(CieLab a, CieLab b) => Math.Sqrt(CieDe76Squared(a, b));

    /// <summary>
    /// Which ΔE the palette matcher uses. CIE76 — here <see cref="HueWeightedSquared"/>, CIE76
    /// with the hue term weighted — is the historical default and is what every number in the
    /// repo was tuned against; CIEDE2000 is the 2000 revision that behaves far
    /// better in the blue region. Kept as a process-wide switch so the diagnostic bench can run
    /// both without threading a parameter through the whole pipeline. Production leaves it at
    /// <see cref="Metric.Cie76"/> — see <c>docs/ciede2000-plan.md</c>.
    /// </summary>
    public enum Metric
    {
        Cie76,
        Ciede2000,
    }

    /// <summary>
    /// The metric <see cref="Match"/> resolves to. Only the palette matcher
    /// (<c>Quantizer.NearestIndex</c>, <c>PaletteReducer</c>) reads it; the neighbour weight and
    /// the sampler statistics stay on CIE76 regardless.
    /// </summary>
    public static Metric MatchingMetric { get; set; } = Metric.Cie76;

    /// <summary>
    /// How much heavier a change of hue counts than an equal loss of chroma, in squared units.
    /// One is plain CIE76 — the euclidean distance charges the same for both, and the eye does
    /// not: a colour that has gone duller reads as the same colour in poorer light, a colour whose
    /// hue has moved reads as the wrong colour. Measured on the bench: 5–10 % of the chromatic
    /// pieces on landscape and landscape-2 were landing more than 40° away in hue, which is what
    /// put acid yellow into a green forest (docs/tsvet-uezzhaet-plan.md).
    /// </summary>
    public const double HueWeight = 3.0;

    /// <summary>
    /// CIE76 with its chromatic half split into the two losses it confuses — how much chroma was
    /// given up, and how far the hue moved — and the hue term weighted by <see cref="HueWeight"/>.
    /// At weight one this is exactly <see cref="CieDe76Squared"/>, which is the point: it is not a
    /// different colour model, only the one degree of freedom euclid nails to unity.
    ///
    /// Nothing has to guard the neutrals: ΔH is bounded by the chroma of the two colours, so for a
    /// near-grey it is small on its own. That is right — grey has no hue to be wrong about.
    /// </summary>
    public static double HueWeightedSquared(CieLab a, CieLab b)
    {
        double dl = a.L - b.L;
        double chromaA = Math.Sqrt((a.A * a.A) + (a.B * a.B));
        double chromaB = Math.Sqrt((b.A * b.A) + (b.B * b.B));
        double dc = chromaA - chromaB;

        double da = a.A - b.A;
        double db = a.B - b.B;
        double dh2 = Math.Max(0.0, (da * da) + (db * db) - (dc * dc));

        return (dl * dl) + (dc * dc) + (HueWeight * dh2);
    }

    /// <summary>
    /// ΔE for palette matching, under whichever <see cref="MatchingMetric"/> is set. Returns the
    /// distance itself, not its square: CIEDE2000 has no monotonic squared form.
    /// </summary>
    public static double Match(CieLab a, CieLab b) =>
        MatchingMetric == Metric.Cie76 ? Math.Sqrt(HueWeightedSquared(a, b)) : CieDe2000(a, b);

    /// <summary>
    /// Squared matching ΔE, for the callers whose cost model is built in squared units
    /// (<c>CoherentMap</c>). Under CIE76 this is the exact square and avoids a square root;
    /// under CIEDE2000 — which has no true squared form — it is <see cref="CieDe2000"/> squared,
    /// which keeps that cost model homogeneous even though the value is not itself a ΔE.
    /// </summary>
    public static double MatchSquared(CieLab a, CieLab b)
    {
        if (MatchingMetric == Metric.Cie76)
        {
            return HueWeightedSquared(a, b);
        }

        double d = CieDe2000(a, b);
        return d * d;
    }

    /// <summary>
    /// CIEDE2000 colour difference with unit weighting (k_L = k_C = k_H = 1). Follows the
    /// formulation of Sharma, Wu &amp; Dalal, "The CIEDE2000 Color-Difference Formula" (2005),
    /// including their arctangent and mean-hue conventions.
    /// </summary>
    public static double CieDe2000(CieLab a, CieLab b)
    {
        double c1 = Math.Sqrt((a.A * a.A) + (a.B * a.B));
        double c2 = Math.Sqrt((b.A * b.A) + (b.B * b.B));
        double cBar = (c1 + c2) / 2.0;

        double cBar7 = Math.Pow(cBar, 7.0);
        double g = 0.5 * (1.0 - Math.Sqrt(cBar7 / (cBar7 + Pow25_7)));

        double a1p = (1.0 + g) * a.A;
        double a2p = (1.0 + g) * b.A;

        double c1p = Math.Sqrt((a1p * a1p) + (a.B * a.B));
        double c2p = Math.Sqrt((a2p * a2p) + (b.B * b.B));

        double h1p = HueDegrees(a.B, a1p);
        double h2p = HueDegrees(b.B, a2p);

        double dLp = b.L - a.L;
        double dCp = c2p - c1p;

        double dhp;
        if (c1p * c2p == 0.0)
        {
            dhp = 0.0;
        }
        else
        {
            dhp = h2p - h1p;
            if (dhp > 180.0)
            {
                dhp -= 360.0;
            }
            else if (dhp < -180.0)
            {
                dhp += 360.0;
            }
        }

        double dHp = 2.0 * Math.Sqrt(c1p * c2p) * Math.Sin(Radians(dhp) / 2.0);

        double lBarp = (a.L + b.L) / 2.0;
        double cBarp = (c1p + c2p) / 2.0;

        double hBarp;
        if (c1p * c2p == 0.0)
        {
            hBarp = h1p + h2p;
        }
        else if (Math.Abs(h1p - h2p) <= 180.0)
        {
            hBarp = (h1p + h2p) / 2.0;
        }
        else if (h1p + h2p < 360.0)
        {
            hBarp = (h1p + h2p + 360.0) / 2.0;
        }
        else
        {
            hBarp = (h1p + h2p - 360.0) / 2.0;
        }

        double t = 1.0
            - (0.17 * Math.Cos(Radians(hBarp - 30.0)))
            + (0.24 * Math.Cos(Radians(2.0 * hBarp)))
            + (0.32 * Math.Cos(Radians((3.0 * hBarp) + 6.0)))
            - (0.20 * Math.Cos(Radians((4.0 * hBarp) - 63.0)));

        double dTheta = 30.0 * Math.Exp(-Math.Pow((hBarp - 275.0) / 25.0, 2.0));
        double cBarp7 = Math.Pow(cBarp, 7.0);
        double rC = 2.0 * Math.Sqrt(cBarp7 / (cBarp7 + Pow25_7));
        double rT = -Math.Sin(Radians(2.0 * dTheta)) * rC;

        double sL = 1.0
            + ((0.015 * Math.Pow(lBarp - 50.0, 2.0))
               / Math.Sqrt(20.0 + Math.Pow(lBarp - 50.0, 2.0)));
        double sC = 1.0 + (0.045 * cBarp);
        double sH = 1.0 + (0.015 * cBarp * t);

        double termL = dLp / sL;
        double termC = dCp / sC;
        double termH = dHp / sH;

        return Math.Sqrt(
            (termL * termL) + (termC * termC) + (termH * termH) + (rT * termC * termH));
    }

    private static readonly double Pow25_7 = Math.Pow(25.0, 7.0);

    private static double Radians(double degrees) => degrees * Math.PI / 180.0;

    private static double HueDegrees(double b, double aPrime)
    {
        if (b == 0.0 && aPrime == 0.0)
        {
            return 0.0;
        }

        double degrees = Math.Atan2(b, aPrime) * 180.0 / Math.PI;
        return degrees >= 0.0 ? degrees : degrees + 360.0;
    }
}
