namespace MosaicGenerator.Core.Domain;

/// <summary>
/// How finely the photograph is resolved, expressed as modules across the panel's short side.
/// Legibility depends on how many modules land on the subject, not on the module size itself:
/// 10 mm tesserae read as detail on a 1.2 m panel and as nothing on a 30 cm one.
/// </summary>
public enum DetailLevel
{
    Draft,
    Standard,
    Detailed,
    Maximum,
}

public static class DetailLevels
{
    public static int TargetAcross(this DetailLevel level) => level switch
    {
        DetailLevel.Draft => 50,
        DetailLevel.Standard => 80,
        DetailLevel.Detailed => 120,
        DetailLevel.Maximum => 160,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };
}
