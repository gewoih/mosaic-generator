using System.Reflection;
using SkiaSharp;

namespace MosaicGenerator.Core.Skia;

/// <summary>
/// The scheme's typeface, embedded rather than looked up by family name: a container image
/// usually ships no fonts at all, and a missing family would silently fall back to blank glyphs.
/// </summary>
internal static class SchemeFont
{
    private const string ResourceName = "MosaicGenerator.Core.SchemeFont.ttf";

    private static readonly Lazy<SKTypeface> Instance = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    public static SKTypeface Typeface => Instance.Value;

    private static SKTypeface Load()
    {
        using Stream stream = typeof(SchemeFont).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing.");

        return SKTypeface.FromStream(stream)
            ?? throw new InvalidOperationException("The embedded scheme font could not be parsed.");
    }
}
