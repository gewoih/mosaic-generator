namespace MosaicGenerator.Web.Services;

public interface IResultStore
{
    string Save(
        StoredResult result,
        byte[] cartoonPng,
        byte[] schemePng,
        byte[] legendPng,
        IReadOnlyDictionary<int, byte[]> ladderCartoons);

    StoredResult? Find(string id);

    byte[]? ReadImage(string id, ResultImage image);

    /// <summary>A baked cartoon at <paramref name="colors"/> shades, or null when that step was not baked.</summary>
    byte[]? ReadLadderCartoon(string id, int colors);
}

public enum ResultImage
{
    Cartoon,
    Scheme,
    Legend,
}
