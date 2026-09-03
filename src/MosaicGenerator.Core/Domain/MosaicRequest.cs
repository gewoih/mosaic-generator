using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MosaicGenerator.Core.Domain;

public sealed record MosaicRequest
{
    public required double PanelWidthMm { get; init; }

    public required double PanelHeightMm { get; init; }

    public required double ModuleWidthMm { get; init; }

    public required double ModuleHeightMm { get; init; }

    public required double GroutWidthMm { get; init; }

    public required string PaletteId { get; init; }

    /// <summary>
    /// Where the crop window sits, as a fraction of the source along each axis: 0.5 centres it.
    /// A photograph almost never puts its subject in the middle, and the panel's aspect rarely
    /// matches the camera's, so the centre crop is a default rather than an answer.
    /// </summary>
    public double CropAnchorX { get; init; } = 0.5;

    public double CropAnchorY { get; init; } = 0.5;

    /// <summary>
    /// Whether shade matching accounts for the joint the tessera will sit in. On by default:
    /// a quarter to a half of every cell is adhesive seen down a slot, and that grey compresses
    /// the panel's range unless the shades are chosen against it. Left off it can be compared
    /// against the naive match.
    /// </summary>
    public bool CompensateJoint { get; init; } = true;

    /// <summary>
    /// Articles the colour ceiling must not discard, however few modules they cover. A handful of
    /// tesserae can carry a whole picture — a beak, a catchlight — and counting alone cannot see
    /// that.
    /// </summary>
    public IReadOnlyCollection<string> PinnedArticles { get; init; } = [];

    public double WastePercent { get; init; } = 25.0;

    /// <summary>Average supplier rate for the whole range, in roubles per kilogram.</summary>
    public decimal PricePerKgRub { get; init; } = 1500m;

    /// <summary>
    /// Ceiling on how many shades the finished work may use. Quantising against a large palette
    /// leaves a tail of colours used once or twice, which reads as no detail at all but costs a
    /// separate article each.
    /// </summary>
    public int MaxColors { get; init; } = 20;

    /// <summary>
    /// Drives the per-module chipping and tone jitter. Left at zero it is derived from the
    /// request itself, so repeating a generation reproduces the previous layout exactly.
    /// </summary>
    public ulong Seed { get; init; }

    public double WasteFactor => 1.0 + (WastePercent / 100.0);

    public ulong EffectiveSeed => Seed != 0 ? Seed : DeriveSeed();

    private ulong DeriveSeed()
    {
        var builder = new StringBuilder()
            .Append(PanelWidthMm.ToString("R", CultureInfo.InvariantCulture)).Append('|')
            .Append(PanelHeightMm.ToString("R", CultureInfo.InvariantCulture)).Append('|')
            .Append(ModuleWidthMm.ToString("R", CultureInfo.InvariantCulture)).Append('|')
            .Append(ModuleHeightMm.ToString("R", CultureInfo.InvariantCulture)).Append('|')
            .Append(GroutWidthMm.ToString("R", CultureInfo.InvariantCulture)).Append('|')
            .Append(CropAnchorX.ToString("R", CultureInfo.InvariantCulture)).Append('|')
            .Append(CropAnchorY.ToString("R", CultureInfo.InvariantCulture)).Append('|')
            .Append(PaletteId);

        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()), digest);
        return BitConverter.ToUInt64(digest) | 1UL;
    }
}
