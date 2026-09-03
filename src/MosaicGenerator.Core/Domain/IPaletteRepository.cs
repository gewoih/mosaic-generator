using System.Diagnostics.CodeAnalysis;

namespace MosaicGenerator.Core.Domain;

public interface IPaletteRepository
{
    IReadOnlyList<Palette> All { get; }

    bool TryGet(string id, [NotNullWhen(true)] out Palette? palette);
}
