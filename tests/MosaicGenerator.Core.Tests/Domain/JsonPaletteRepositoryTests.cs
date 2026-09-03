using MosaicGenerator.Core.Colors;
using MosaicGenerator.Core.Domain;

namespace MosaicGenerator.Core.Tests.Domain;

public class JsonPaletteRepositoryTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateTempSubdirectory("mosaic-palette-tests").FullName;

    [Fact]
    public void LoadsAPaletteAndResolvesItById()
    {
        Write("one.json", """
            {
              "id": "demo",
              "name": "Демо",
              "colors": [
                { "article": "SM-1", "name": "Белая", "hex": "#FFFFFF",
                  "thicknessMm": 8, "densityKgPerM3": 2500 }
              ]
            }
            """);

        var repository = new JsonPaletteRepository(_directory);

        Assert.True(repository.TryGet("demo", out Palette? palette));
        Assert.Equal("Демо", palette!.Name);
        Assert.Equal(Rgb.FromHex("#FFFFFF").ToBytes(), palette.Colors[0].Rgb.ToBytes());
    }

    [Fact]
    public void AnUnknownIdIsReportedRatherThanThrown()
    {
        Write("one.json", MinimalPalette("demo"));

        var repository = new JsonPaletteRepository(_directory);

        Assert.False(repository.TryGet("nope", out _));
        Assert.False(repository.TryGet("", out _));
        Assert.False(repository.TryGet(null!, out _));
    }

    [Fact]
    public void MalformedJsonFailsAtStartupWithTheOffendingFileNamed()
    {
        Write("broken.json", """{ "id": "x", "name": "y", "colors": [ { "hex": "not-a-colour" } ] }""");

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => new JsonPaletteRepository(_directory));

        Assert.Contains("broken.json", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyDirectoryFailsAtStartup()
    {
        Assert.Throws<InvalidOperationException>(() => new JsonPaletteRepository(_directory));
    }

    [Fact]
    public void AMissingDirectoryFailsAtStartup()
    {
        Assert.Throws<DirectoryNotFoundException>(() =>
            new JsonPaletteRepository(Path.Combine(_directory, "absent")));
    }

    [Fact]
    public void TheShippedSmaltPaletteLoadsAndIsUsable()
    {
        string shipped = Path.Combine(RepositoryRoot(), "src", "MosaicGenerator.Web", "Data", "palettes");

        var repository = new JsonPaletteRepository(shipped);

        Assert.True(repository.TryGet("artworker-smalt", out Palette? palette));
        Assert.True(palette!.Colors.Count >= 150, $"only {palette.Colors.Count} colours");

        // Articles have to be unique: the consumption table sorts on them to break ties.
        Assert.Equal(
            palette.Colors.Count,
            palette.Colors.Select(c => c.Article).Distinct(StringComparer.Ordinal).Count());

        // A palette that clusters in one corner of Lab cannot quantise a photograph.
        double[] lightness = [.. palette.Colors.Select(c => c.Lab.L)];
        Assert.True(lightness.Min() < 20, $"no dark shades, darkest L* is {lightness.Min():F1}");
        Assert.True(lightness.Max() > 90, $"no light shades, lightest L* is {lightness.Max():F1}");
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static string MinimalPalette(string id) => $$"""
        {
          "id": "{{id}}",
          "name": "Демо",
          "colors": [
            { "article": "SM-1", "name": "Белая", "hex": "#FFFFFF",
              "thicknessMm": 8, "densityKgPerM3": 2500 }
          ]
        }
        """;

    private void Write(string name, string content) =>
        File.WriteAllText(Path.Combine(_directory, name), content);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MosaicGenerator.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
