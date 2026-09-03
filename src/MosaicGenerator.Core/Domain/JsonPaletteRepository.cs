using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace MosaicGenerator.Core.Domain;

/// <summary>Loads every palette in a directory once, at startup; palettes are immutable afterwards.</summary>
public sealed class JsonPaletteRepository : IPaletteRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Dictionary<string, Palette> _byId;

    public JsonPaletteRepository(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Palette directory '{directory}' does not exist.");
        }

        var palettes = new List<Palette>();
        foreach (string path in Directory.EnumerateFiles(directory, "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            palettes.Add(Load(path));
        }

        if (palettes.Count == 0)
        {
            throw new InvalidOperationException($"No palettes found in '{directory}'.");
        }

        _byId = palettes.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        All = palettes;
    }

    public IReadOnlyList<Palette> All { get; }

    public bool TryGet(string id, [NotNullWhen(true)] out Palette? palette)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            palette = null;
            return false;
        }

        return _byId.TryGetValue(id, out palette);
    }

    private static Palette Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        try
        {
            return JsonSerializer.Deserialize<Palette>(stream, SerializerOptions)
                ?? throw new InvalidOperationException($"Palette '{path}' is empty.");
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException)
        {
            throw new InvalidOperationException($"Palette '{path}' is malformed: {exception.Message}", exception);
        }
    }
}
