namespace MosaicGenerator.Web.Services;

public interface IResultStore
{
    string Save(StoredResult result, byte[] previewPng, byte[] schemePng);

    StoredResult? Find(string id);

    byte[]? ReadImage(string id, ResultImage image);
}

public enum ResultImage
{
    Preview,
    Scheme,
}
