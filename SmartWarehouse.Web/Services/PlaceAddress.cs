using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.Web.Services;

/// <summary>
/// Адрес места словами — вместо шифра «SK1-1-C3-1-1».
/// Area — этаж или зона («1-й этаж», «У ворот»); Full — «Стеллаж C3 · полка 1 · ячейка 1»;
/// Short — для чипов и телефона: «C3 · п.1 · яч.1»; Rest — то, что ниже стеллажа («полка 1 · ячейка 1»);
/// Note — описание, которое ввели руками (не автоимя из импорта); Code — шифр для сверки с QR-меткой.
/// </summary>
public sealed record PlaceAddress(
    string? Area,
    string Full,
    string Short,
    string? Rest,
    string? Note,
    string Code,
    int GroupId,
    string GroupTitle)
{
    /// <summary>Подсказка при наведении: этаж, адрес, описание, шифр.</summary>
    public string Title => string.Join(" · ",
        new[] { Area != Full ? Area : null, Full, Note, Code }.Where(s => !string.IsNullOrWhiteSpace(s)));

    /// <summary>Место без дерева (не загрузилось) — показываем шифр как есть.</summary>
    public static PlaceAddress OfCode(string code, string? name = null, int id = 0) =>
        new(null, code, code, null, string.IsNullOrWhiteSpace(name) ? null : name.Trim(), code, id, code);

    /// <summary>Адрес по цепочке мест от верхнего уровня до самого места.</summary>
    public static PlaceAddress Build(IReadOnlyList<LocationNodeDto> chain)
    {
        if (chain.Count == 0)
            throw new ArgumentException("Пустая цепочка мест.", nameof(chain));

        var place = chain[^1];
        string? area = null;
        var full = new List<string>();
        var brief = new List<string>();
        var rest = new List<string>();
        string? note = null;
        var group = chain[0];
        var seenRack = false;

        foreach (var n in chain)
        {
            var custom = CustomName(n);
            if (custom is not null)
                note = custom; // самое глубокое описание — оно точнее

            switch (n.Type)
            {
                case LocationType.Zone when full.Count == 0 && area is null:
                    area = custom ?? OpeningImportService.PlaceName(LocationType.Zone, n.Code);
                    // Этажи (1, 2) в короткой форме не пишем — есть в подсказке; прочие зоны («у ворот») пишем.
                    if (!n.Code.All(char.IsDigit))
                        brief.Add(area.ToLowerInvariant());
                    group = n;
                    if (custom is not null)
                        note = null; // своё имя зоны уже стоит в Area
                    break;
                case LocationType.Rack:
                    full.Add($"Стеллаж {n.Code}");
                    brief.Add(n.Code);
                    group = n;
                    seenRack = true;
                    break;
                case LocationType.Shelf:
                    var shelf = IsFloor(n) ? "на полу" : $"полка {n.Code}";
                    full.Add(shelf);
                    brief.Add(IsFloor(n) ? "пол" : $"п.{n.Code}");
                    rest.Add(shelf);
                    if (!seenRack) group = n;
                    break;
                case LocationType.Cell:
                    full.Add($"ячейка {n.Code}");
                    brief.Add($"яч.{n.Code}");
                    rest.Add($"ячейка {n.Code}");
                    if (!seenRack && rest.Count == 1) group = n;
                    break;
                default:
                    var label = custom ?? $"{Ui.LocType(n.Type).ToLowerInvariant()} {n.Code}";
                    full.Add(label);
                    brief.Add(n.Code);
                    rest.Add(label);
                    break;
            }
        }

        if (full.Count > 0)
            full[0] = Capitalize(full[0]);
        var fullText = full.Count > 0 ? string.Join(" · ", full) : area ?? place.FullCode;
        var shortText = brief.Count > 0 ? string.Join(" · ", brief) : fullText;
        if (note is not null && note == area)
            note = null;

        var groupTitle = group.Type == LocationType.Rack
            ? string.Join(" · ", new[] { area, $"Стеллаж {group.Code}" }.Where(s => s is not null))
            : group.Type == LocationType.Zone ? area ?? group.FullCode : fullText;

        return new PlaceAddress(area, fullText, shortText,
            group.Id == place.Id ? null : rest.Count > 0 ? string.Join(" · ", rest) : null,
            note, place.FullCode, group.Id, groupTitle);
    }

    /// <summary>Полка 0 — под стеллажом, на полу.</summary>
    public static bool IsFloor(LocationNodeDto n) => n.Type == LocationType.Shelf && n.Code == "0";

    /// <summary>Описание, введённое человеком; автоимена из импорта («Полка 2», «Ячейка 1») — не описание.</summary>
    public static string? CustomName(LocationNodeDto n)
    {
        if (string.IsNullOrWhiteSpace(n.Name))
            return null;
        var name = n.Name.Trim();
        var auto = OpeningImportService.PlaceName(n.Type, n.Code);
        if (string.Equals(name, auto, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, n.Code, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, $"{Ui.LocType(n.Type)} {n.Code}", StringComparison.OrdinalIgnoreCase))
            return null;
        return name;
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
