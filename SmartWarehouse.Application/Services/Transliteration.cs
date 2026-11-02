namespace SmartWarehouse.Application.Services;

/// <summary>
/// Коды (склад, место хранения, артикул) пишутся только латиницей: так их «напечатает» ручной сканер при любой раскладке.
/// Здесь — перевод старых кодов с кириллицей в латиницу (по звучанию) с английскими сокращениями вместо русских:
/// «СК1-A-01» → «WH1-A-01» (warehouse), «ЗЧ-000005» → «SP-000005» (spare part).
/// Тем же правилом миграция перевела коды в базе, а сканер находит позицию и по старой наклейке.
/// </summary>
public static class Transliteration
{
    /// <summary>Русские сокращения в начале кода → английские.</summary>
    public static readonly IReadOnlyList<(string Ru, string En)> Prefixes =
    [
        ("ЗЧ-", "SP-"),
        ("СК", "WH")
    ];

    /// <summary>Буква кириллицы (заглавная и строчная) → латиница.</summary>
    public static readonly IReadOnlyDictionary<char, string> Letters = Build();

    private static Dictionary<char, string> Build()
    {
        var upper = new Dictionary<char, string>
        {
            ['А'] = "A", ['Б'] = "B", ['В'] = "V", ['Г'] = "G", ['Д'] = "D", ['Е'] = "E", ['Ё'] = "E", ['Ж'] = "ZH",
            ['З'] = "Z", ['И'] = "I", ['Й'] = "Y", ['К'] = "K", ['Л'] = "L", ['М'] = "M", ['Н'] = "N", ['О'] = "O",
            ['П'] = "P", ['Р'] = "R", ['С'] = "S", ['Т'] = "T", ['У'] = "U", ['Ф'] = "F", ['Х'] = "KH", ['Ц'] = "TS",
            ['Ч'] = "CH", ['Ш'] = "SH", ['Щ'] = "SHCH", ['Ъ'] = "", ['Ы'] = "Y", ['Ь'] = "", ['Э'] = "E", ['Ю'] = "YU",
            ['Я'] = "YA"
        };
        var all = new Dictionary<char, string>(upper);
        foreach (var (ru, en) in upper)
            all[char.ToLowerInvariant(ru)] = en.ToLowerInvariant();
        return all;
    }

    public static bool HasCyrillic(string text) => text.Any(c => Letters.ContainsKey(c));

    /// <summary>Код латиницей: сначала сокращения в начале (регистр не важен), затем буквы по звучанию.</summary>
    public static string ToLatin(string code)
    {
        var rest = code;
        var head = "";
        foreach (var (ru, en) in Prefixes)
        {
            if (rest.StartsWith(ru, StringComparison.OrdinalIgnoreCase))
            {
                head = en;
                rest = rest[ru.Length..];
                break;
            }
        }

        var sb = new System.Text.StringBuilder(head, code.Length + 4);
        foreach (var c in rest)
            sb.Append(Letters.TryGetValue(c, out var latin) ? latin : c.ToString());
        return sb.ToString();
    }
}
