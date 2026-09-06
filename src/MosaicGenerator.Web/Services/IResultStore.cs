namespace MosaicGenerator.Web.Services;

public interface IResultStore
{
    string Save(StoredResult result, byte[] cartoonPng, byte[] schemePng, byte[] legendPng);

    StoredResult? Find(string id);

    byte[]? ReadImage(string id, ResultImage image);
}

public enum ResultImage
{
    Cartoon,
    Scheme,
    Legend,
}
