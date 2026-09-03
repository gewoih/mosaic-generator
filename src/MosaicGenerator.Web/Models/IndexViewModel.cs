using MosaicGenerator.Core.Domain;
using MosaicGenerator.Web.Services;

namespace MosaicGenerator.Web.Models;

/// <summary>
/// The parameter block, shared by the upload page and the result page. Settling a panel is a
/// dozen passes over the same controls, so they are the same controls in both places.
/// </summary>
public sealed record ParametersViewModel
{
    public required GenerateFormModel Form { get; init; }

    public required IReadOnlyList<Palette> Palettes { get; init; }

    /// <summary>
    /// Module range, joint rule and detail targets, straight from the core. Emitted rather than
    /// restated in the script so the page cannot drift from what the generator will actually do.
    /// </summary>
    public required string LayoutDataJson { get; init; }
}

public sealed record IndexViewModel
{
    public required ParametersViewModel Parameters { get; init; }

    public required int MaxUploadMegabytes { get; init; }

    public GenerateFormModel Form => Parameters.Form;
}

public sealed record ResultViewModel
{
    public required string Id { get; init; }

    public required StoredResult Stored { get; init; }

    public required ParametersViewModel Parameters { get; init; }
}
