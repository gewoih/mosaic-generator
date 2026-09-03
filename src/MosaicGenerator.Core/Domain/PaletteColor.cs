using System.Text.Json.Serialization;
using MosaicGenerator.Core.Colors;

namespace MosaicGenerator.Core.Domain;

/// <summary>
/// One smalt shade. Price is deliberately absent: suppliers quote an average rate per kilogram
/// for the whole range rather than a figure per shade, so it belongs to the request.
/// The derived colour spaces are computed once in the constructor: palettes are shared as
/// singletons across requests, and lazy fields of this size cannot be torn-read safely.
/// </summary>
public sealed class PaletteColor
{
    [JsonConstructor]
    public PaletteColor(
        string article,
        string name,
        string hex,
        double thicknessMm,
        double densityKgPerM3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(article);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(thicknessMm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(densityKgPerM3);

        Article = article;
        Name = name;
        Hex = hex;
        ThicknessMm = thicknessMm;
        DensityKgPerM3 = densityKgPerM3;

        Rgb = Rgb.FromHex(hex);
        Lab = Rgb.ToLab();
    }

    public string Article { get; }

    public string Name { get; }

    public string Hex { get; }

    public double ThicknessMm { get; }

    public double DensityKgPerM3 { get; }

    [JsonIgnore]
    public Rgb Rgb { get; }

    [JsonIgnore]
    public CieLab Lab { get; }
}
