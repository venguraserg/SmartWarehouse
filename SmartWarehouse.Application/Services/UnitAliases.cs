namespace SmartWarehouse.Application.Services;

/// <summary>
/// Единицы из 1С пишутся по-разному («пог. м», «м.п.», «Комплект», «тн») — приводим к одному виду,
/// чтобы не плодить единицы и чтобы позиция из накладной совпала с уже заведённой.
/// </summary>
public static class UnitAliases
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["шт."] = "шт", ["штук"] = "шт", ["штука"] = "шт",
        ["пог. м"] = "м", ["пог.м"] = "м", ["пог.м."] = "м", ["м.п."] = "м", ["м.п"] = "м", ["мп"] = "м", ["м.пог"] = "м", ["м."] = "м",
        ["тн"] = "т", ["тн."] = "т", ["тонн"] = "т",
        ["кг."] = "кг", ["л."] = "л",
        ["комплект"] = "компл", ["компл."] = "компл", ["комп"] = "компл", ["к-т"] = "компл",
        ["уп"] = "упак", ["уп."] = "упак", ["упк"] = "упак", ["упак."] = "упак", ["упаковка"] = "упак",
        ["пар"] = "пара", ["рул."] = "рул", ["рулон"] = "рул",
        ["тыс.шт"] = "тыс. шт", ["тыс.шт."] = "тыс. шт", ["тыс.м."] = "тыс. м", ["тыс.м"] = "тыс. м",
        ["100 штук"] = "100 шт", ["м²"] = "м2", ["кв.м"] = "м2", ["кв. м"] = "м2",
    };

    /// <summary>Единицы, в которых бывает дробное количество (для новых единиц).</summary>
    public static readonly IReadOnlySet<string> Fractional = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "м", "кг", "л", "т", "м2", "м3", "км", "см", "тыс. м", "тыс. шт", "100 шт", "шт/м3" };

    public static string Normalize(string? unit)
    {
        var u = System.Text.RegularExpressions.Regex.Replace(unit ?? "", @"\s+", " ").Trim();
        if (Map.TryGetValue(u, out var mapped))
            return mapped;
        // «ШТ», «Кг» → нижний регистр; прочее — как есть.
        return u.Length <= 4 ? u.ToLowerInvariant() : u;
    }
}
