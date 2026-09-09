using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Pipeline;
using MosaicGenerator.Core.Skia;
using MosaicGenerator.Core.Tests.Support;
using SkiaSharp;

namespace MosaicGenerator.Core.Tests.Pipeline;

public class MosaicRecolorTests
{
    private readonly MosaicGenerationService _service = new(
        new SkiaImageLoader(), new SkiaMosaicRenderer(), new MosaicGenerationOptions());

    private readonly Palette _palette = PaletteFactory.OfHex(
        "#F4F1E8", "#C9A24B", "#B33125", "#2E6FA8", "#4B7A34", "#141311");

    [Fact]
    public void AnEmptySwapMapRedrawsExactlyWhatTheGenerationProduced()
    {
        MosaicResult result = Generate();

        RecolorResult redrawn = _service.Recolor(Request(result, []));

        Assert.Equal(result.CartoonPng, redrawn.CartoonPng);
        Assert.Equal(result.SchemePng, redrawn.SchemePng);
        Assert.Equal(result.LegendPng, redrawn.LegendPng);
        Assert.Equal(
            result.Report.Lines.Select(l => (l.Color.Article, l.ModuleCount)),
            redrawn.Report.Lines.Select(l => (l.Color.Article, l.ModuleCount)));
    }

    [Fact]
    public void SwappingOneArticleForAnotherMovesItsPiecesAndNothingElse()
    {
        MosaicResult result = Generate();
        Assert.True(result.Report.Lines.Count >= 2);

        string from = result.Report.Lines[0].Color.Article;
        string to = result.Report.Lines[1].Color.Article;
        int fromCount = result.Report.Lines[0].ModuleCount;
        int toCount = result.Report.Lines[1].ModuleCount;

        RecolorResult redrawn = _service.Recolor(Request(result, new()
        {
            [IndexOf(from)] = IndexOf(to),
        }));

        Assert.DoesNotContain(redrawn.Report.Lines, l => l.Color.Article == from);
        Assert.Equal(
            fromCount + toCount,
            redrawn.Report.Lines.Single(l => l.Color.Article == to).ModuleCount);
        Assert.Equal(result.Report.TotalModules, redrawn.Report.TotalModules);
        Assert.NotEqual(result.CartoonPng, redrawn.CartoonPng);

        using SKBitmap? cartoon = SKBitmap.Decode(redrawn.CartoonPng);
        Assert.NotNull(cartoon);
    }

    [Fact]
    public void ASwapChainCollapsesToItsEnd()
    {
        MosaicResult result = Generate();
        Assert.True(result.Report.Lines.Count >= 3);

        string a = result.Report.Lines[0].Color.Article;
        string b = result.Report.Lines[1].Color.Article;
        string c = result.Report.Lines[2].Color.Article;

        RecolorResult redrawn = _service.Recolor(Request(result, new()
        {
            [IndexOf(a)] = IndexOf(b),
            [IndexOf(b)] = IndexOf(c),
        }));

        Assert.DoesNotContain(redrawn.Report.Lines, l => l.Color.Article == a || l.Color.Article == b);
        Assert.Equal(result.Report.TotalModules, redrawn.Report.TotalModules);
    }

    private MosaicResult Generate()
    {
        using Stream photo = MakePhoto(400, 300);
        return _service.Generate(photo, RequestFactory.Request(400, 300, maxColors: 100), _palette);
    }

    private RecolorRequest Request(MosaicResult result, Dictionary<int, int> swaps) => new()
    {
        Layout = result.Layout,
        Palette = _palette,
        Tesserae = result.Tesserae,
        BaseIndices = result.FinalIndices,
        Swaps = swaps,
        WasteFactor = 1.25,
        PricePerKgRub = 3200m,
    };

    private int IndexOf(string article)
    {
        for (int i = 0; i < _palette.Colors.Count; i++)
        {
            if (_palette.Colors[i].Article == article)
            {
                return i;
            }
        }

        throw new ArgumentException($"No {article} in the test palette.", nameof(article));
    }

    private static Stream MakePhoto(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bitmap))
        {
            using var paint = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0),
                    new SKPoint(width, height),
                    [SKColors.White, SKColors.OrangeRed, SKColors.DarkSlateBlue, SKColors.Black],
                    null,
                    SKShaderTileMode.Clamp),
            };
            canvas.DrawRect(new SKRect(0, 0, width, height), paint);
        }

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return new MemoryStream(data.ToArray());
    }
}
