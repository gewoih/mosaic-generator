using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Tests.Support;
using MosaicGenerator.Core.Validation;

namespace MosaicGenerator.Core.Tests.Validation;

public class MosaicRequestValidatorTests
{
    private readonly ValidationLimits _limits = new();

    [Fact]
    public void AReasonableRequestPasses()
    {
        Assert.Empty(Validate(RequestFactory.Request(panelWidth: 1200, panelHeight: 800, module: 20, grout: 3)));
    }

    [Theory]
    [InlineData(50, 800, nameof(MosaicRequest.PanelWidthMm))]
    [InlineData(4000, 800, nameof(MosaicRequest.PanelWidthMm))]
    [InlineData(1200, 50, nameof(MosaicRequest.PanelHeightMm))]
    [InlineData(1200, 4000, nameof(MosaicRequest.PanelHeightMm))]
    public void PanelDimensionsAreBounded(double width, double height, string field)
    {
        Assert.Contains(Validate(RequestFactory.Request(width, height)), e => e.Field == field);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(150)]
    public void ModuleSizeIsBounded(double module)
    {
        Assert.Contains(
            Validate(RequestFactory.Request(module: module)),
            e => e.Field == nameof(MosaicRequest.ModuleWidthMm));
    }

    [Fact]
    public void FineWorkDownToTwoMillimetresIsAllowed()
    {
        // Smalt is cut this small for faces and other detailed passages; refusing it would rule
        // out exactly the work that needs the most modules.
        Assert.Empty(Validate(RequestFactory.Request(panelWidth: 300, panelHeight: 300, module: 2, grout: 1)));
    }

    [Fact]
    public void ThePricePerKilogramIsBounded()
    {
        Assert.Empty(Validate(RequestFactory.Request(pricePerKg: 1500m)));
        Assert.Contains(
            Validate(RequestFactory.Request(pricePerKg: -1m)),
            e => e.Field == nameof(MosaicRequest.PricePerKgRub));
    }

    [Fact]
    public void GroutAndWasteAreBounded()
    {
        Assert.Contains(
            Validate(RequestFactory.Request(grout: 25)),
            e => e.Field == nameof(MosaicRequest.GroutWidthMm));
        Assert.Contains(
            Validate(RequestFactory.Request(wastePercent: 250)),
            e => e.Field == nameof(MosaicRequest.WastePercent));
        Assert.Contains(
            Validate(RequestFactory.Request(wastePercent: -1)),
            e => e.Field == nameof(MosaicRequest.WastePercent));
    }

    [Fact]
    public void AModuleWiderThanThePanelIsCalledOutDirectly()
    {
        // Unreachable under the default limits, where the largest module equals the smallest
        // panel. The guard exists for hosts that widen the module range, so it is tested there.
        var limits = new ValidationLimits { MinPanelMm = 50, MaxModuleMm = 200 };

        IReadOnlyList<ValidationError> errors = MosaicRequestValidator.Validate(
            RequestFactory.Request(panelWidth: 60, panelHeight: 60, module: 120, grout: 3), limits);

        ValidationError error = Assert.Single(errors);
        Assert.Contains("Модуль больше панно", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AModuleExactlyTheSizeOfThePanelStillYieldsOneModule()
    {
        Assert.Empty(Validate(RequestFactory.Request(panelWidth: 100, panelHeight: 100, module: 100, grout: 3)));
    }

    [Fact]
    public void TooManyModulesAreRefusedBeforeAnythingIsRendered()
    {
        IReadOnlyList<ValidationError> errors =
            Validate(RequestFactory.Request(panelWidth: 3000, panelHeight: 3000, module: 5, grout: 1));

        Assert.Contains(errors, e => e.Message.Contains("модулей", StringComparison.Ordinal));
    }

    [Fact]
    public void AnExtremeAspectRatioIsRefused()
    {
        IReadOnlyList<ValidationError> errors =
            Validate(RequestFactory.Request(panelWidth: 3000, panelHeight: 100));

        Assert.Contains(errors, e => e.Message.Contains("вытянутое", StringComparison.Ordinal));
    }

    [Fact]
    public void FiveToOneIsStillAccepted()
    {
        Assert.Empty(Validate(RequestFactory.Request(panelWidth: 1500, panelHeight: 300)));
    }

    [Fact]
    public void TheColourCeilingIsBounded()
    {
        Assert.Empty(Validate(RequestFactory.Request(maxColors: 20)));
        Assert.Contains(
            Validate(RequestFactory.Request(maxColors: 1)),
            e => e.Field == nameof(MosaicRequest.MaxColors));
        Assert.Contains(
            Validate(RequestFactory.Request(maxColors: 500)),
            e => e.Field == nameof(MosaicRequest.MaxColors));
    }

    [Fact]
    public void AMissingPaletteIsReported()
    {
        Assert.Contains(
            Validate(RequestFactory.Request(paletteId: "  ")),
            e => e.Field == nameof(MosaicRequest.PaletteId));
    }

    [Fact]
    public void RangeErrorsShortCircuitTheDerivedChecks()
    {
        // A zero module would make the grid arithmetic divide by zero; validation must stop first.
        IReadOnlyList<ValidationError> errors = Validate(RequestFactory.Request(module: 0));

        Assert.All(errors, e => Assert.Contains("допустимо от", e.Message, StringComparison.Ordinal));
    }

    private IReadOnlyList<ValidationError> Validate(MosaicRequest request) =>
        MosaicRequestValidator.Validate(request, _limits);
}
