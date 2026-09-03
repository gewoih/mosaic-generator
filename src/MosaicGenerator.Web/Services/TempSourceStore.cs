using Microsoft.Extensions.Options;
using MosaicGenerator.Core.Imaging;
using MosaicGenerator.Web.Options;

namespace MosaicGenerator.Web.Services;

/// <summary>
/// Uploads live in a temp directory keyed by a fresh GUID and are swept once they age out, on the
/// same terms as the results they feed. Stored as they arrived rather than decoded: the decoder is
/// the one part of the chain that must keep seeing exactly what the user handed over, signature
/// checks and all.
/// </summary>
public sealed class TempSourceStore : ISourceStore
{
    private const string SourceFileName = "source.bin";

    private readonly string _root;
    private readonly TimeSpan _lifetime;
    private readonly TimeProvider _clock;
    private readonly ILogger<TempSourceStore> _logger;

    public TempSourceStore(
        IOptions<MosaicOptions> options,
        TimeProvider clock,
        ILogger<TempSourceStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _root = Path.Combine(Path.GetTempPath(), "mosaic-generator-sources");
        _lifetime = options.Value.ResultLifetime;
        _clock = clock;
        _logger = logger;

        Directory.CreateDirectory(_root);
    }

    public string Save(Stream photo)
    {
        ArgumentNullException.ThrowIfNull(photo);

        Sweep();

        string id = Guid.NewGuid().ToString("N");
        string directory = Path.Combine(_root, id);
        Directory.CreateDirectory(directory);

        using (FileStream file = File.Create(Path.Combine(directory, SourceFileName)))
        {
            photo.CopyTo(file);
        }

        return id;
    }

    public Stream? Open(string id) =>
        TryResolve(id, out string? path) ? File.OpenRead(path) : null;

    /// <summary>
    /// Resolves an id supplied by the client. The id must be a bare GUID and the resolved path is
    /// re-checked against the root, so a traversal attempt cannot reach outside the store.
    /// </summary>
    private bool TryResolve(string id, out string path)
    {
        path = string.Empty;

        if (!Guid.TryParseExact(id, "N", out _))
        {
            return false;
        }

        string candidate = Path.GetFullPath(Path.Combine(_root, id, SourceFileName));
        if (!candidate.StartsWith(Path.GetFullPath(_root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return false;
        }

        if (!File.Exists(candidate))
        {
            return false;
        }

        path = candidate;
        return true;
    }

    private void Sweep()
    {
        DateTimeOffset cutoff = _clock.GetUtcNow() - _lifetime;

        foreach (string directory in Directory.EnumerateDirectories(_root))
        {
            try
            {
                if (Directory.GetCreationTimeUtc(directory) < cutoff.UtcDateTime)
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A concurrent request may already be deleting it, or a file may still be open.
                _logger.LogDebug(exception, "Could not sweep {Directory}.", directory);
            }
        }
    }

    /// <summary>Content type for serving a stored upload back to the crop preview.</summary>
    public static string ContentTypeOf(ReadOnlySpan<byte> header) => ImageFormatDetector.Detect(header) switch
    {
        ImageFormat.Jpeg => "image/jpeg",
        ImageFormat.Png => "image/png",
        ImageFormat.WebP => "image/webp",
        _ => "application/octet-stream",
    };
}
