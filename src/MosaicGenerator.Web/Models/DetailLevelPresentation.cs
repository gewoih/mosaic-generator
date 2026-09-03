using MosaicGenerator.Core.Domain;

namespace MosaicGenerator.Web.Models;

public static class DetailLevelPresentation
{
    public static string Label(DetailLevel level) => level switch
    {
        DetailLevel.Draft => "Черновой",
        DetailLevel.Detailed => "Детальный",
        DetailLevel.Maximum => "Максимальный",
        _ => "Стандартный",
    };

    public static string Hint(DetailLevel level) => level switch
    {
        DetailLevel.Draft => "Прикинуть композицию, крупные тессеры",
        DetailLevel.Detailed => "Мелкие детали: глаза, клюв, блики",
        DetailLevel.Maximum => "Предельно мелко, очень трудоёмкий набор",
        _ => "Портреты, животные, пейзаж",
    };

    public static string LimitNote(ModuleChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);

        return choice.Limit switch
        {
            ModuleLimit.PanelTooSmall =>
                $"Запрошено {choice.RequestedAcross} модулей по короткой стороне, получилось " +
                $"{choice.ModulesAcrossShortSide}: панно мало для этого уровня даже на самой мелкой " +
                "смальте. Увеличьте размер панно или понизьте детализацию.",
            ModuleLimit.ModuleCountCapped =>
                $"Запрошено {choice.RequestedAcross} модулей по короткой стороне, получилось " +
                $"{choice.ModulesAcrossShortSide}: более мелкий модуль вышел бы за предел числа " +
                "модулей в работе.",
            _ => string.Empty,
        };
    }
}
