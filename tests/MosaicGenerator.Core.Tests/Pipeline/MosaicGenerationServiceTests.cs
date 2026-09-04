using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Pipeline;
using MosaicGenerator.Core.Rendering;
using MosaicGenerator.Core.Skia;
using MosaicGenerator.Core.Tests.Support;
using SkiaSharp;

namespace MosaicGenerator.Core.Tests.Pipeline;

public class MosaicGenerationServiceTests
{
    private readonly MosaicGenerationService _service = new(
        new SkiaImageLoader(), new SkiaMosaicRenderer(), new MosaicGenerationOptions());

    private readonly Palette _palette = PaletteFactory.OfHex(
        "#F4F1E8", "#C9A24B", "#B33125", "#2E6FA8", "#4B7A34", "#141311");

    [Fact]
    public void TheWholeChainProducesTwoUsablePngsAndAMatchingReport()
    {
        using Stream photo = Photo(1200, 800);
        MosaicRequest request = RequestFactory.Request(panelWidth: 1200, panelHeight: 800, module: 20, grout: 3);

        MosaicResult result = _service.Generate(photo, request, _palette);

        Assert.Equal(52, result.Layout.Columns);
        Assert.Equal(34, result.Layout.Rows);

        // The streamline layout no longer holds exactly Columns x Rows tesserae, but every tessera
        // it does hold is accounted for in the consumption table, and the count stays in the same
        // ballpark as the nominal grid.
        Assert.Equal(result.TesseraCount, result.Report.TotalModules);
        Assert.InRange(result.TesseraCount, (int)(1768 * 0.6), (int)(1768 * 1.4));
        Assert.True(result.CutTesseraCount >= 0 && result.CutTesseraCount <= result.TesseraCount);

        using SKBitmap? cartoon = SKBitmap.Decode(result.CartoonPng);
        using SKBitmap? scheme = SKBitmap.Decode(result.SchemePng);
        Assert.NotNull(cartoon);
        Assert.NotNull(scheme);
        Assert.Equal(result.Cartoon.PixelWidth, cartoon!.Width);
        Assert.Equal(result.Scheme.PixelWidth, scheme!.Width);

        // The cartoon is rendered at twice the scheme's pixels per step — it prints 1:1.
        Assert.True(cartoon.Width > scheme.Width);
    }

    [Fact]
    public void TheRequestedGeometryIsHonouredExactlyAndTheLeftoverBecomesAMargin()
    {
        using Stream photo = Photo(1000, 1000);
        MosaicRequest request = RequestFactory.Request(panelWidth: 1000, panelHeight: 1000, module: 20, grout: 3);

        MosaicResult result = _service.Generate(photo, request, _palette);

        Assert.Equal(20.0, result.Layout.ModuleWidthMm, 1e-9);
        Assert.Equal(3.0, result.Layout.GroutWidthMm, 1e-9);
        Assert.Equal(1000.0, result.Layout.PanelWidthMm, 1e-9);
        Assert.Equal(7.0, result.Layout.MarginXMm, 1e-9);
    }

    [Fact]
    public void RegeneratingWithTheSameInputsReproducesTheSameBytes()
    {
        MosaicRequest request = RequestFactory.Request(panelWidth: 400, panelHeight: 300);

        using Stream first = Photo(400, 300);
        using Stream second = Photo(400, 300);

        MosaicResult a = _service.Generate(first, request, _palette);
        MosaicResult b = _service.Generate(second, request, _palette);

        Assert.Equal(a.CartoonPng, b.CartoonPng);
        Assert.Equal(a.SchemePng, b.SchemePng);
    }

    [Fact]
    public void OnlyPaletteColoursReachTheLayout()
    {
        using Stream photo = Photo(400, 300);

        MosaicResult result = _service.Generate(photo, RequestFactory.Request(400, 300), _palette);

        Assert.All(
            result.Report.Lines,
            line => Assert.Contains(line.Color, _palette.Colors));
    }

    [Fact]
    public void APhotoOfASinglePaletteColourQuantisesToThatColourAlone()
    {
        using Stream photo = SolidPhoto(400, 300, new SKColor(0x2E, 0x6F, 0xA8));

        MosaicResult result = _service.Generate(photo, RequestFactory.Request(400, 300), _palette);

        Assert.Equal("#2E6FA8", Assert.Single(result.Report.Lines).Color.Hex);
    }

    [Fact]
    public void PhotoAspectIsAbsorbedByTheCentreCropNotByStretching()
    {
        // A tall photo against a wide panel: the crop takes the middle band, so the left and right
        // halves of the layout still differ the way the middle of the photo does.
        using Stream photo = HalvedPhoto(400, 1200);

        MosaicResult result = _service.Generate(photo, RequestFactory.Request(1200, 400), _palette);

        Assert.Equal(1200.0 / 400.0, result.Layout.PanelWidthMm / result.Layout.PanelHeightMm, 1e-9);
        Assert.True(result.Report.Lines.Count >= 2);
    }

    [Fact]
    public void TheColourCeilingBoundsTheConsumptionTable()
    {
        using Stream photo = Photo(400, 300);
        MosaicRequest request = RequestFactory.Request(400, 300, maxColors: 3);

        MosaicResult result = _service.Generate(photo, request, _palette);

        Assert.True(result.Report.Lines.Count <= 3, $"got {result.Report.Lines.Count} lines");
        Assert.Equal(result.TesseraCount, result.Report.TotalModules);
        Assert.True(result.ColorsBeforeReduction >= result.Report.Lines.Count);
        Assert.True(result.ModulesReassigned > 0);
    }

    [Fact]
    public void AGenerousCeilingLeavesTheQuantiserUntouched()
    {
        using Stream photo = Photo(400, 300);

        MosaicResult result = _service.Generate(
            photo, RequestFactory.Request(400, 300, maxColors: 100), _palette);

        Assert.Equal(result.ColorsBeforeReduction, result.Report.Lines.Count);
        Assert.Equal(0, result.ModulesReassigned);
    }

    [Fact]
    public void AnInvalidRequestIsRefusedBeforeTheImageIsEvenDecoded()
    {
        using Stream photo = Photo(400, 300);

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            _service.Generate(photo, RequestFactory.Request(panelWidth: 10), _palette));

        Assert.Contains("Ширина панно", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, photo.Position);
    }

    [Fact]
    public void ChangingTheWasteAllowanceMovesTheCostButNotTheLayout()
    {
        MosaicRequest lean = RequestFactory.Request(400, 300, wastePercent: 0);
        MosaicRequest generous = RequestFactory.Request(400, 300, wastePercent: 25);

        using Stream a = Photo(400, 300);
        using Stream b = Photo(400, 300);

        MosaicResult lite = _service.Generate(a, lean, _palette);
        MosaicResult full = _service.Generate(b, generous, _palette);

        Assert.Equal(lite.CartoonPng, full.CartoonPng);
        Assert.Equal(lite.Report.TotalModules, full.Report.TotalModules);
        Assert.Equal(lite.Report.TotalMassKg * 1.25, full.Report.TotalMassKg, 1e-9);
    }

    private static Stream Photo(int width, int height) =>
        Encode(width, height, canvas =>
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
        });

    private static Stream SolidPhoto(int width, int height, SKColor color) =>
        Encode(width, height, canvas => canvas.Clear(color));

    private static Stream HalvedPhoto(int width, int height) =>
        Encode(width, height, canvas =>
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(new SKRect(0, 0, width / 2f, height), paint);
        });

    private static Stream Encode(int width, int height, Action<SKCanvas> draw)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bitmap))
        {
            draw(canvas);
        }

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return new MemoryStream(data.ToArray());
    }
}
