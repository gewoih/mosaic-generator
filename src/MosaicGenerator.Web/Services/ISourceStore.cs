namespace MosaicGenerator.Web.Services;

/// <summary>
/// Keeps the uploaded photograph alive between generations. Settling a panel takes a dozen passes
/// over the crop, the detail level and the colour ceiling, and making the file be re-picked for
/// every one of them turns an afternoon of judgement into an afternoon of file dialogs.
/// </summary>
public interface ISourceStore
{
    /// <summary>Stores the upload as it arrived and returns its id.</summary>
    string Save(Stream photo);

    /// <summary>Opens a stored upload for reading, or null when it has aged out.</summary>
    Stream? Open(string id);
}
