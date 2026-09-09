using System.Text.Json;
using Microsoft.Extensions.Options;
using MosaicGenerator.Web.Options;

namespace MosaicGenerator.Web.Services;

/// <summary>
/// Results live in a temp directory keyed by a fresh GUID and are swept once they age out. No
/// database in this iteration, so a restart or a sweep simply loses old results — regenerating is
/// cheap and the pipeline is deterministic.
/// </summary>
public sealed class TempResultStore : IResultStore
{
    private const string ManifestFileName = "result.json";
    private const string RecolorFileName = "recolor.json";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly string _root;
    private readonly TimeSpan _lifetime;
    private readonly TimeProvider _clock;
    private readonly ILogger<TempResultStore> _logger;

    public TempResultStore(
        IOptions<MosaicOptions> options,
        TimeProvider clock,
        ILogger<TempResultStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _root = Path.Combine(Path.GetTempPath(), "mosaic-generator");
        _lifetime = options.Value.ResultLifetime;
        _clock = clock;
        _logger = logger;

        Directory.CreateDirectory(_root);
    }

    public string Save(
        StoredResult result,
        RecolorState recolorState,
        byte[] cartoonPng,
        byte[] schemePng,
        byte[] legendPng,
        IReadOnlyDictionary<int, byte[]> ladderCartoons)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(recolorState);
        ArgumentNullException.ThrowIfNull(ladderCartoons);

        Sweep();

        string id = Guid.NewGuid().ToString("N");
        string directory = Path.Combine(_root, id);
        Directory.CreateDirectory(directory);

        File.WriteAllBytes(Path.Combine(directory, FileNameFor(ResultImage.Cartoon)), cartoonPng);
        File.WriteAllBytes(Path.Combine(directory, FileNameFor(ResultImage.Scheme)), schemePng);
        File.WriteAllBytes(Path.Combine(directory, FileNameFor(ResultImage.Legend)), legendPng);
        foreach ((int colors, byte[] png) in ladderCartoons)
        {
            File.WriteAllBytes(Path.Combine(directory, LadderCartoonName(colors)), png);
        }

        File.WriteAllText(
            Path.Combine(directory, ManifestFileName),
            JsonSerializer.Serialize(result, SerializerOptions));

        File.WriteAllText(
            Path.Combine(directory, RecolorFileName),
            JsonSerializer.Serialize(recolorState, SerializerOptions));

        return id;
    }

    public StoredResult? Find(string id)
    {
        if (!TryResolve(id, ManifestFileName, out string? path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<StoredResult>(File.ReadAllText(path), SerializerOptions);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            _logger.LogWarning(exception, "Stored result {Id} could not be read.", id);
            return null;
        }
    }

    public RecolorState? FindRecolorState(string id)
    {
        if (!TryResolve(id, RecolorFileName, out string? path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RecolorState>(File.ReadAllText(path), SerializerOptions);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            _logger.LogWarning(exception, "Recolor state for {Id} could not be read.", id);
            return null;
        }
    }

    public byte[]? ReadImage(string id, ResultImage image) =>
        TryResolve(id, FileNameFor(image), out string? path) ? File.ReadAllBytes(path) : null;

    public byte[]? ReadLadderCartoon(string id, int colors)
    {
        if (colors is < 1 or > 999)
        {
            return null;
        }

        return TryResolve(id, LadderCartoonName(colors), out string? path) ? File.ReadAllBytes(path) : null;
    }

    private static string LadderCartoonName(int colors) => $"cartoon-c{colors}.png";

    private static string FileNameFor(ResultImage image) => image switch
    {
        ResultImage.Cartoon => "cartoon.png",
        ResultImage.Scheme => "scheme.png",
        ResultImage.Legend => "legend.png",
        _ => throw new ArgumentOutOfRangeException(nameof(image), image, null),
    };

    /// <summary>
    /// Resolves an id supplied by the client. The id must be a bare GUID and the resolved path is
    /// re-checked against the root, so a traversal attempt cannot reach outside the store.
    /// </summary>
    private bool TryResolve(string id, string fileName, out string path)
    {
        path = string.Empty;

        if (!Guid.TryParseExact(id, "N", out _))
        {
            return false;
        }

        string candidate = Path.GetFullPath(Path.Combine(_root, id, fileName));
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
}
