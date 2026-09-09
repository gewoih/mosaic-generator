using MosaicGenerator.Core.Grid;

namespace MosaicGenerator.Web.Services;

public interface IResultStore
{
    string Save(
        StoredResult result,
        RecolorState recolorState,
        byte[] cartoonPng,
        byte[] schemePng,
        byte[] legendPng,
        IReadOnlyDictionary<int, byte[]> ladderCartoons);

    StoredResult? Find(string id);

    /// <summary>
    /// The geometry and base mapping kept so an article can be swapped on the result page without
    /// re-running the pipeline. Null once the result has aged out or if it was never written.
    /// </summary>
    RecolorState? FindRecolorState(string id);

    byte[]? ReadImage(string id, ResultImage image);

    /// <summary>A baked cartoon at <paramref name="colors"/> shades, or null when that step was not baked.</summary>
    byte[]? ReadLadderCartoon(string id, int colors);
}

/// <summary>Base geometry and mapping for the result page's manual article swap.</summary>
public sealed record RecolorState
{
    public required IReadOnlyList<int> BaseIndices { get; init; }

    public required IReadOnlyList<Tessera> Tesserae { get; init; }
}

public enum ResultImage
{
    Cartoon,
    Scheme,
    Legend,
}
