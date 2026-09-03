namespace MosaicGenerator.Core.Domain;

/// <summary>
/// The physical grid: how many modules fit in the panel, and where the leftover goes.
/// The requested panel size, module size and grout width are all honoured exactly;
/// whatever does not divide evenly becomes a margin split around the perimeter.
/// </summary>
public sealed record MosaicLayout
{
    // The cell count is a floor division on values that rarely divide cleanly in binary
    // floating point; without this nudge an exact fit such as 986 / 23 lands on 42.999...
    private const double FitEpsilon = 1e-9;

    private MosaicLayout()
    {
    }

    public required int Columns { get; init; }

    public required int Rows { get; init; }

    public required double PanelWidthMm { get; init; }

    public required double PanelHeightMm { get; init; }

    public required double ModuleWidthMm { get; init; }

    public required double ModuleHeightMm { get; init; }

    public required double GroutWidthMm { get; init; }

    public double StepXMm => ModuleWidthMm + GroutWidthMm;

    public double StepYMm => ModuleHeightMm + GroutWidthMm;

    /// <summary>Width covered by modules and the grout between them, excluding the margins.</summary>
    public double FieldWidthMm => (Columns * StepXMm) - GroutWidthMm;

    public double FieldHeightMm => (Rows * StepYMm) - GroutWidthMm;

    public double MarginXMm => (PanelWidthMm - FieldWidthMm) / 2.0;

    public double MarginYMm => (PanelHeightMm - FieldHeightMm) / 2.0;

    public double FieldAspect => FieldWidthMm / FieldHeightMm;

    public int TotalModules => Columns * Rows;

    public static MosaicLayout Compute(MosaicRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        int columns = FitCount(request.PanelWidthMm, request.ModuleWidthMm, request.GroutWidthMm);
        int rows = FitCount(request.PanelHeightMm, request.ModuleHeightMm, request.GroutWidthMm);

        if (columns < 1 || rows < 1)
        {
            throw new ArgumentException(
                "The module does not fit inside the panel.", nameof(request));
        }

        return new MosaicLayout
        {
            Columns = columns,
            Rows = rows,
            PanelWidthMm = request.PanelWidthMm,
            PanelHeightMm = request.PanelHeightMm,
            ModuleWidthMm = request.ModuleWidthMm,
            ModuleHeightMm = request.ModuleHeightMm,
            GroutWidthMm = request.GroutWidthMm,
        };
    }

    /// <summary>
    /// n modules span n * (module + grout) - grout, because the last module has no grout after it.
    /// </summary>
    public static int FitCount(double panelMm, double moduleMm, double groutMm)
    {
        double step = moduleMm + groutMm;
        return (int)Math.Floor(((panelMm + groutMm) / step) + FitEpsilon);
    }

    /// <summary>Left edge of the module in column <paramref name="column"/>, measured from the panel edge.</summary>
    public double ModuleLeftMm(int column) => MarginXMm + (column * StepXMm);

    public double ModuleTopMm(int row) => MarginYMm + (row * StepYMm);
}
