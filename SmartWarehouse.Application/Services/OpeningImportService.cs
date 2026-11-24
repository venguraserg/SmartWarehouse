using System.Text.RegularExpressions;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Application.Services;

public interface IOpeningImportService
{
    /// <summary>
    /// Ввод начальных остатков из файла «Начальные остатки — готово (1С на …)».
    /// <paramref name="apply"/> = false — только проверка: что будет создано и какие строки пропустятся.
    /// </summary>
    Task<OpeningImportResult> ImportAsync(OpeningFile file, int warehouseId, bool apply, ImportActor actor, CancellationToken ct = default);
}

/// <summary>Кто загружает: записывается в документы ввода остатков.</summary>
public record ImportActor(int? EmployeeId, string? Name);

/// <summary>
/// Начальные остатки склада: единицы, позиции, места хранения (из адресов «1.A1.0.2» — этаж.стеллаж.полка.ячейка)
/// создаются, если их нет; остатки вводятся обычными документами, чтобы журнал и партии были как при работе:
/// приход «Ввод остатков» — партии с ценой и датой документа 1С, сразу в свои места, МОЛ партии — кладовщик из файла.
/// Партии других МОЛ (подотчёт специалистов) не переносятся: на склад — только накладной на перемещение на кладовщика.
/// Повторный запуск позиции и места не дублирует, а остатки — добавит ещё раз: загружать один раз.
/// </summary>
public class OpeningImportService(
    ICatalogService catalog,
    ILocationService locations,
    IEmployeeService employees,
    IStockService stock,
    IMolService mols) : IOpeningImportService
{
    /// <summary>
    /// «Сортировка»: сюда попадает товар, у которого в файле нет адреса или адрес не по схеме.
    /// Прежнее место пишется в примечание партии, кладовщик раскладывает со страницы «Приёмка».
    /// </summary>
    public const string SortingCode = "SORT";

    public const string SortingName = "Сортировка — разместить вручную";

    private const int MaxLevels = 4;

    public async Task<OpeningImportResult> ImportAsync(OpeningFile file, int warehouseId, bool apply, ImportActor actor, CancellationToken ct = default)
    {
        var issues = new List<ImportIssue>(file.Issues);
        var notes = new List<string>();
        var warehouse = (await locations.ListWarehousesAsync(ct)).FirstOrDefault(w => w.Id == warehouseId)
            ?? throw new NotFoundException("Склад не найден — создайте его в «Складах и местах».");

        var units = (await catalog.ListUnitsAsync(ct)).ToDictionary(u => u.Code, StringComparer.OrdinalIgnoreCase);
        var items = (await catalog.ListItemsAsync(null, null, all: true, ct)).ToDictionary(i => i.Sku, StringComparer.OrdinalIgnoreCase);
        // МОЛ склада — только кладовщики (сотрудник с учётной записью и ролью «Кладовщик»).
        var storekeeperIds = (await mols.ListAsync(ct)).Select(m => m.EmployeeId).ToHashSet();
        var storekeepers = (await employees.ListAsync(null, includeInactive: true, ct)).Where(e => storekeeperIds.Contains(e.Id)).ToList();
        var notStorekeeper = new Dictionary<string, (int Line, int Count)>(StringComparer.OrdinalIgnoreCase);

        // ---------- 1. Проверка строк ----------
        var valid = new List<Planned>();
        var fileItems = new Dictionary<string, (string Name, string Unit)>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in file.Rows)
        {
            string sku;
            try
            {
                sku = CodeRules.NormalizeSku(row.Code);
            }
            catch (DomainException e)
            {
                issues.Add(new ImportIssue(row.Line, row.Code, e.Message));
                continue;
            }

            var name = row.Name.Trim();
            if (name.Length == 0)
            {
                issues.Add(new ImportIssue(row.Line, sku, "Нет наименования."));
                continue;
            }
            if (name.Length > 256)
            {
                issues.Add(new ImportIssue(row.Line, sku, "Наименование длиннее 256 символов — обрезано.", Skipped: false));
                name = name[..256];
            }

            var unit = row.Unit.Trim();
            if (unit.Length == 0)
            {
                issues.Add(new ImportIssue(row.Line, sku, "Нет единицы измерения."));
                continue;
            }
            if (row.Quantity <= 0)
            {
                issues.Add(new ImportIssue(row.Line, sku, $"Количество {row.Quantity:0.###} — должно быть больше нуля."));
                continue;
            }
            if (row.Value < 0)
            {
                issues.Add(new ImportIssue(row.Line, sku, "Отрицательная стоимость."));
                continue;
            }

            // Позиция: одна на код; единица у позиции одна.
            if (fileItems.TryGetValue(sku, out var known))
            {
                if (!string.Equals(known.Unit, unit, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new ImportIssue(row.Line, sku, $"Единица «{unit}», а выше в файле у этого кода — «{known.Unit}»."));
                    continue;
                }
            }
            else
            {
                fileItems[sku] = (name, unit);
            }
            if (items.TryGetValue(sku, out var existing) && !string.Equals(existing.UnitCode, unit, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ImportIssue(row.Line, sku, $"Позиция {sku} уже есть с единицей «{existing.UnitCode}», в файле — «{unit}»."));
                continue;
            }

            var fractional = units.TryGetValue(unit, out var u) ? u.IsFractional : UnitAliases.IsFractional(unit);
            if (!fractional && row.Quantity != decimal.Truncate(row.Quantity))
            {
                issues.Add(new ImportIssue(row.Line, sku, $"Дробное количество {row.Quantity:0.###} в «{unit}» — уточните (в 1С так числится)."));
                continue;
            }

            // Партии других МОЛ (подотчёт специалистов) на склад не переносятся.
            if (row.Custody)
            {
                issues.Add(new ImportIssue(row.Line, sku, $"Партия МОЛ «{row.Mol}» (не склад) — не переносится."));
                continue;
            }

            // МОЛ учётной партии — кладовщик: из файла, а если в файле не указан — единственный кладовщик.
            int? molId = null;
            if (!row.OffBook)
            {
                var mol = string.IsNullOrWhiteSpace(row.Mol) ? null : row.Mol.Trim();
                molId = mol is null
                    ? storekeepers.Count == 1 ? storekeepers[0].Id : null
                    : PersonNames.Find(storekeepers, mol)?.Id;
                if (molId is null)
                {
                    var key = mol ?? "(не указан)";
                    notStorekeeper[key] = notStorekeeper.TryGetValue(key, out var seen) ? (seen.Line, seen.Count + 1) : (row.Line, 1);
                    continue;
                }
            }

            var path = ParseAddress(row.Address);
            if (path is null)
                issues.Add(new ImportIssue(row.Line, sku, (string.IsNullOrWhiteSpace(row.Address)
                    ? "Нет адреса хранения"
                    : $"Адрес «{row.Address.Trim()}» не по схеме") + " — в «Сортировку».", Skipped: false));
            else if (path.Length > MaxLevels)
            {
                issues.Add(new ImportIssue(row.Line, sku, $"Адрес {row.Address}: больше {MaxLevels} уровней (этаж.стеллаж.полка.ячейка) — в «Сортировку».", Skipped: false));
                path = null;
            }

            // Вне учёта без стоимости — партия без цены (в оценке склада не участвует).
            decimal? price = row.OffBook && row.Value == 0
                ? null
                : decimal.Round(row.Value / row.Quantity, 4, MidpointRounding.AwayFromZero);
            valid.Add(new Planned(row, sku, name, unit, price, path) { MolId = molId });
        }
        foreach (var (mol, (line, count)) in notStorekeeper)
            issues.Add(new ImportIssue(line, null, mol == "(не указан)"
                ? $"МОЛ не указан, а кладовщиков {(storekeepers.Count == 0 ? "нет" : "несколько")} — пропущено партий: {count}. " +
                  "Укажите МОЛ в файле или заведите кладовщика (сотрудник с учётной записью и ролью «Кладовщик»)."
                : $"МОЛ «{mol}» — не кладовщик склада (нужен сотрудник с учётной записью и ролью «Кладовщик») — пропущено партий: {count}."));

        // ---------- 2. Места хранения из адресов ----------
        // Схема: этаж (зона).стеллаж.полка.ячейка; товар может лежать на любом уровне (на полке без ящиков — «2.B1.2»).
        var tree = await locations.GetLocationTreeAsync(warehouse.Id, ct);
        var existingByCode = Flatten(tree).ToDictionary(n => n.FullCode, StringComparer.OrdinalIgnoreCase);
        foreach (var p in valid.Where(p => p.Path is not null))
            p.Target = p.Path;
        var neededNodes = valid.Where(p => p.Target is not null)
            .SelectMany(p => Enumerable.Range(1, p.Target!.Length).Select(n => string.Join('.', p.Target!.Take(n))))
            .Distinct()
            .ToList();
        var newNodes = neededNodes.Where(a => !existingByCode.ContainsKey(FullCode(warehouse.Code, a))).ToList();
        var needSorting = valid.Any(ToSort) && !existingByCode.ContainsKey(FullCode(warehouse.Code, SortingCode));

        var newUnits = valid.Select(p => p.Unit).Distinct(StringComparer.OrdinalIgnoreCase).Where(u => !units.ContainsKey(u)).ToList();
        var newItems = valid.Select(p => p.Sku).Distinct(StringComparer.OrdinalIgnoreCase).Where(s => !items.ContainsKey(s)).ToList();

        if (!apply)
        {
            return Result(false, file, valid, issues, notes, newItems.Count, items, newUnits.Count,
                newNodes.Count + (needSorting ? 1 : 0), 0, []);
        }

        // ---------- 4. Загрузка ----------
        foreach (var code in newUnits)
        {
            var unit = await catalog.CreateUnitAsync(new CreateUnitRequest(code, code, UnitAliases.IsFractional(code)), ct);
            units[unit.Code] = unit;
        }

        foreach (var sku in newItems)
        {
            var first = valid.First(p => string.Equals(p.Sku, sku, StringComparison.OrdinalIgnoreCase));
            var description = first.Row.Description is { Length: > 0 } d ? (d.Length > 2000 ? d[..2000] : d) : null;
            items[sku] = await catalog.CreateItemAsync(
                new CreateItemRequest(sku, first.Name, null, units[first.Unit].Id, 0, null, description), ct);
        }

        // Места: сверху вниз; тип — по уровню адреса (этаж, стеллаж, полка, ячейка).
        var failed = new List<string>();
        foreach (var address in neededNodes.OrderBy(a => a.Count(c => c == '.')).ThenBy(a => a, StringComparer.Ordinal))
        {
            var full = FullCode(warehouse.Code, address);
            if (existingByCode.ContainsKey(full))
                continue;
            var parts = address.Split('.');
            int? parentId = parts.Length == 1 ? null : existingByCode[FullCode(warehouse.Code, string.Join('.', parts[..^1]))].Id;
            var type = TypeOf(parts.Length);
            var description = PlaceName(type, parts[^1]);
            if (failed.Any(f => address.StartsWith(f + ".", StringComparison.Ordinal)))
                continue;
            try
            {
                var created = await locations.AddLocationAsync(warehouse.Id, new CreateLocationRequest(parentId, type, parts[^1], description), ct);
                existingByCode[created.FullCode] = new LocationNodeDto(created.Id, parentId, type, parts[^1], created.FullCode, description, []);
            }
            catch (DomainException e)
            {
                // Например, в существующей ячейке лежит товар — вложить в неё нельзя.
                failed.Add(address);
                notes.Add($"Место {full} не создано: {e.Message}");
            }
        }
        foreach (var p in valid.Where(p => p.Target is not null && failed.Any(f => string.Join('.', p.Target!) == f
                     || string.Join('.', p.Target!).StartsWith(f + ".", StringComparison.Ordinal))))
        {
            issues.Add(new ImportIssue(p.Row.Line, p.Sku, $"Место для адреса {p.Row.Address} не создано (см. замечания) — в «Сортировку».", Skipped: false));
            p.Target = null;
        }

        int? sortingId = null;
        if (valid.Any(ToSort))
        {
            sortingId = existingByCode.TryGetValue(FullCode(warehouse.Code, SortingCode), out var sorting)
                ? sorting.Id
                : (await locations.AddLocationAsync(warehouse.Id,
                    new CreateLocationRequest(null, LocationType.Cell, SortingCode, SortingName), ct)).Id;
            notes.Add($"В «Сортировку» ({FullCode(warehouse.Code, SortingCode)}): {valid.Count(ToSort)} партий. " +
                      "Прежнее место — в примечании партии; разложить на странице «Приёмка».");
        }

        // Приход: партии с ценой, датой и документом 1С. Не больше 500 строк в документе.
        // Учётное и «вне учёта» — разными документами: признак вне учёта ставится на документ и его партии.
        var documents = new List<long>();
        // У каждого кладовщика-МОЛ — свои документы: МОЛ ставится на партии документа.
        var molNames = storekeepers.ToDictionary(e => e.Id, e => e.ShortName);
        foreach (var ((offBook, molId), chunk) in valid
                     .GroupBy(p => (p.Row.OffBook, p.MolId))
                     .OrderBy(g => g.Key.OffBook).ThenBy(g => g.Key.MolId)
                     .SelectMany(g => g.Chunk(StockService.MaxLines).Select(c => (g.Key, c))))
        {
            var lines = chunk.Select(p => new DocumentLineRequest(
                    items[p.Sku].Id, p.Row.Quantity, null,
                    ToSort(p) ? sortingId : existingByCode[FullCode(warehouse.Code, string.Join('.', p.Target!))].Id,
                    UnitPrice: p.Price,
                    BatchDate: p.Row.DocumentDate is DateOnly date ? new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null,
                    BatchNote: ToSort(p) ? SortingNote(p.Row) : BatchNote(p.Row)))
                .ToList();
            var doc = await stock.PostDocumentAsync(new CreateDocumentRequest(
                MovementType.Receipt, offBook ? "Ввод остатков вне учёта" : "Ввод остатков из 1С", null, actor.Name,
                (offBook ? "Начальные остатки вне учёта (б/у, неучтённое — в 1С нет)" : "Начальные остатки (импорт из 1С)")
                    + (molId is int m ? $"; МОЛ {molNames[m]}" : ""),
                lines, PerformedById: actor.EmployeeId, OffBook: offBook, Origin: BatchOrigin.Opening, MolId: molId), ct);
            documents.Add(doc.Id);
        }

        return Result(true, file, valid, issues, notes, newItems.Count, items, newUnits.Count,
            newNodes.Count + (needSorting ? 1 : 0), 0, documents);
    }

    private static OpeningImportResult Result(bool applied, OpeningFile file, List<Planned> valid, List<ImportIssue> issues,
        List<string> notes, int itemsNew, Dictionary<string, ItemDto> items, int unitsNew, int locationsNew, int employeesNew, List<long> documents)
    {
        var distinct = valid.Select(p => p.Sku).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return new OpeningImportResult(
            applied,
            file.Rows.Count + file.Issues.Count(i => i.Skipped),
            valid.Count,
            itemsNew,
            distinct - itemsNew,
            unitsNew,
            locationsNew,
            employeesNew,
            valid.Count(p => !p.Row.Custody),
            valid.Count(p => p.Row.Custody),
            valid.Count(ToSort),
            valid.Count(p => p.Row.OffBook),
            decimal.Round(valid.Sum(p => p.Row.Value), 2),
            issues.OrderBy(i => i.Line).ToList(),
            notes,
            documents);
    }

    /// <summary>Партия без места: адреса нет или он не по схеме.</summary>
    private static bool ToSort(Planned p) => p.Target is null;

    /// <summary>«Прежнее место: шкаф свар; ТТН … от …» — чтобы кладовщик знал, где искать.</summary>
    public static string SortingNote(OpeningRow row)
    {
        var place = string.IsNullOrWhiteSpace(row.Address) ? "не указано" : Regex.Replace(row.Address.Trim(), @"\s+", " ");
        var rest = BatchNote(row);
        var text = $"Прежнее место: {place}" + (rest is null ? "" : $"; {rest}");
        return text.Length <= NoteMax ? text : text[..(NoteMax - 1)] + "…";
    }

    private const int NoteMax = 256;

    private static string? BatchNote(OpeningRow row)
    {
        var parts = new[] { row.Document, row.Note }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim());
        var text = string.Join("; ", parts);
        if (row.DocumentDate is DateOnly d && row.Document is { } doc && !doc.Contains(" от "))
            text = $"{doc} от {d:dd.MM.yyyy}" + (string.IsNullOrWhiteSpace(row.Note) ? "" : $"; {row.Note!.Trim()}");
        return text.Length == 0 ? null : text;
    }

    /// <summary>«2A.2.1.3» → [2A, 2, 1, 3]; null — не по схеме (части — латинские буквы и цифры).</summary>
    public static string[]? ParseAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return null;
        var parts = address.Trim().ToUpperInvariant().Split('.');
        if (parts.Length < 1 || parts.Any(p => p.Length == 0 || p.Length > CodeRules.MaxPartLength || !p.All(char.IsAsciiLetterOrDigit)))
            return null;
        return parts;
    }

    public static string FullCode(string warehouseCode, string address) =>
        warehouseCode + CodeRules.Separator + address.Replace('.', CodeRules.Separator);

    /// <summary>Уровень адреса → тип места: 1 — этаж (зона), 2 — стеллаж, 3 — полка, 4 — ячейка.</summary>
    public static LocationType TypeOf(int level) => level switch
    {
        1 => LocationType.Zone,
        2 => LocationType.Rack,
        3 => LocationType.Shelf,
        _ => LocationType.Cell
    };

    /// <summary>Название места: «1-й этаж», «У ворот», «Стеллаж A1», «Полка 0», «Ячейка 2».</summary>
    public static string PlaceName(LocationType type, string code) => type switch
    {
        LocationType.Zone => code switch
        {
            "1" => "1-й этаж",
            "2" => "2-й этаж",
            "V" => "У ворот",
            _ => $"Зона {code}"
        },
        LocationType.Rack => $"Стеллаж {code}",
        LocationType.Shelf => $"Полка {code}",
        _ => $"Ячейка {code}"
    };

    private static IEnumerable<LocationNodeDto> Flatten(IEnumerable<LocationNodeDto> nodes) =>
        nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));

    private sealed class Planned(OpeningRow row, string sku, string name, string unit, decimal? price, string[]? path)
    {
        public OpeningRow Row { get; } = row;
        public string Sku { get; } = sku;
        public string Name { get; } = name;
        public string Unit { get; } = unit;
        public decimal? Price { get; } = price;
        public string[]? Path { get; } = path;
        public string[]? Target { get; set; }
        public int? MolId { get; init; }
    }
}

/// <summary>ФИО в 1С «Рудаков М.А.» ↔ сотрудник приложения (фамилия и инициалы).</summary>
public static class PersonNames
{
    /// <summary>«Рудаков М.А.» → (Рудаков, М., А.); null — не похоже на «Фамилия И.О.».</summary>
    public static (string Last, string First, string? Middle)? Parse(string? mol)
    {
        if (string.IsNullOrWhiteSpace(mol))
            return null;
        var m = System.Text.RegularExpressions.Regex.Match(mol.Trim(), @"^(?<last>[А-ЯЁA-Z][\p{L}-]+)\s+(?<f>\p{Lu})\.\s*(?:(?<m>\p{Lu})\.?)?$");
        if (!m.Success)
            return null;
        return (m.Groups["last"].Value, m.Groups["f"].Value + ".", m.Groups["m"].Success ? m.Groups["m"].Value + "." : null);
    }

    /// <summary>Ключ сравнения: фамилия + первые буквы имени и отчества, без регистра.</summary>
    public static string Key(string last, string? first, string? middle) =>
        (last.Trim() + Initial(first) + Initial(middle)).ToLowerInvariant().Replace('ё', 'е');

    public static EmployeeDto? Find(IEnumerable<EmployeeDto> staff, string mol)
    {
        if (Parse(mol) is not { } p)
            return null;
        var key = Key(p.Last, p.First, p.Middle);
        var matches = staff.Where(e => Key(e.LastName, e.FirstName, e.MiddleName) == key).ToList();
        return matches.FirstOrDefault(e => e.IsActive) ?? matches.FirstOrDefault();
    }

    private static string Initial(string? part) => string.IsNullOrWhiteSpace(part) ? "" : part.Trim()[..1];
}
