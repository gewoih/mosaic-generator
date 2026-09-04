using System.ComponentModel.DataAnnotations;

namespace MosaicGenerator.Web.Options;

public sealed class MosaicOptions
{
    public const string SectionName = "Mosaic";

    /// <summary>Path to the palette directory, relative to the content root unless rooted.</summary>
    [Required]
    public string PaletteDirectory { get; set; } = Path.Combine("Data", "palettes");

    [Range(1, 100)]
    public int MaxUploadMegabytes { get; set; } = 10;

    [Range(1_000_000, 500_000_000)]
    public long MaxDeclaredPixels { get; set; } = 200_000_000;

    [Range(100_000, 100_000_000)]
    public long MaxDecodedPixels { get; set; } = 24_000_000;

    [Range(4, 200)]
    public int CartoonPixelsPerStep { get; set; } = 96;

    [Range(8, 200)]
    public int SchemePixelsPerStep { get; set; } = 48;

    [Range(500, 20_000)]
    public int MaxLongSidePx { get; set; } = 6000;

    [Range(1_000_000, 200_000_000)]
    public long MaxTotalPixels { get; set; } = 30_000_000;

    [Range(0, 100)]
    public double DefaultWastePercent { get; set; } = 25;

    /// <summary>How long a generated result stays on disk before the next request sweeps it away.</summary>
    public TimeSpan ResultLifetime { get; set; } = TimeSpan.FromHours(1);

    public long MaxUploadBytes => MaxUploadMegabytes * 1024L * 1024L;
}
