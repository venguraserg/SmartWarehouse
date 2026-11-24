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
/// Начальные остатки: единицы, позиции, места хранения (из адресов «2A.2.1.3»), сотрудники-МОЛ — создаются,
/// если их нет; остатки вводятся обычными документами, чтобы журнал и партии были как при работе:
///   приход «Ввод остатков» — партии с ценой и датой документа 1С, сразу в свои ячейки;
///   партии других МОЛ — с пометкой МОЛ: в ячейку по адресу, а без адреса — в «Место не указано».
/// Повторный запуск позиции и места не дублирует, а остатки — добавит ещё раз: загружать один раз.
/// </summary>
public class OpeningImportService(
    ICatalogService catalog,
    ILocationService locations,
    IEmployeeService employees,
    IStockService stock,
    IMolService molService) : IOpeningImportService
{
    /// <summary>
    /// «Сортировка»: сюда попадает товар, у которого в файле нет адреса или адрес не по схеме.
    /// Прежнее место пишется в примечание партии, кладовщик раскладывает со страницы «Приёмка».
    /// </summary>
    public const string SortingCode = "SORT";

    public const string SortingName = "Сортировка — разместить вручную";

    /// <summary>Код вложенного места для товара, лежащего на полке, у которой есть ячейки.</summary>
    public const string LooseCode = "0";

    private const int MaxLevels = 4;

    public async Task<OpeningImportResult> ImportAsync(OpeningFile file, int warehouseId, bool apply, ImportActor actor, CancellationToken ct = default)
    {
        var issues = new List<ImportIssue>(file.Issues);
        var notes = new List<string>();
        var warehouse = (await locations.ListWarehousesAsync(ct)).FirstOrDefault(w => w.Id == warehouseId)
            ?? throw new NotFoundException("Склад не найден — создайте его в «Складах и местах».");

        var units = (await catalog.ListUnitsAsync(ct)).ToDictionary(u => u.Code, StringComparer.OrdinalIgnoreCase);
        var items = (await catalog.ListItemsAsync(null, null, all: true, ct)).ToDictionary(i => i.Sku, StringComparer.OrdinalIgnoreCase);
        var staff = (await employees.ListAsync(null, includeInactive: true, ct)).ToList();

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

            // Партии других МОЛ: по адресу — в ячейку, без адреса — в «Место не указано» (найти и разместить).
            var path = ParseAddress(row.Address);
            if (!row.Custody)
            {
                if (path is null)
                    issues.Add(new ImportIssue(row.Line, sku, (string.IsNullOrWhiteSpace(row.Address)
                        ? "Нет адреса хранения"
                        : $"Адрес «{row.Address.Trim()}» не по схеме") + " — в «Сортировку».", Skipped: false));
            }
            else if (PersonNames.Parse(row.Mol) is null)
            {
                issues.Add(new ImportIssue(row.Line, sku, $"МОЛ «{row.Mol}» — нужно «Фамилия И.О.»."));
                continue;
            }

            // Вне учёта без стоимости — партия без цены (в оценке склада не участвует).
            decimal? price = row.OffBook && row.Value == 0
                ? null
                : decimal.Round(row.Value / row.Quantity, 4, MidpointRounding.AwayFromZero);
            valid.Add(new Planned(row, sku, name, unit, price, path));
        }

        // ---------- 2. Места хранения из адресов ----------
        var tree = await locations.GetLocationTreeAsync(warehouse.Id, ct);
        var existingByCode = Flatten(tree).ToDictionary(n => n.FullCode, StringComparer.OrdinalIgnoreCase);
        var plan = PlanLocations(valid.Where(p => p.Path is not null).Select(p => p.Path!).ToList());
        foreach (var p in valid.Where(p => p.Path is not null))
            p.Target = plan.Targets[string.Join('.', p.Path!)];
        foreach (var (address, target) in plan.Targets.Where(t => t.Value.Length > t.Key.Split('.').Length))
            notes.Add($"Адрес {address}: у него есть вложенные места — товар положен в {string.Join('.', target)} («без ячейки»).");
        foreach (var p in valid.Where(p => p.Target is { Length: > MaxLevels }))
        {
            issues.Add(new ImportIssue(p.Row.Line, p.Sku, $"Адрес {p.Row.Address}: больше {MaxLevels} уровней вложенности — в «Сортировку».", Skipped: false));
            p.Target = null;
        }
        var neededNodes = valid.Where(p => p.Target is not null)
            .SelectMany(p => Enumerable.Range(1, p.Target!.Length).Select(n => string.Join('.', p.Target!.Take(n))))
            .Distinct()
            .ToList();
        var newNodes = neededNodes.Where(a => !existingByCode.ContainsKey(FullCode(warehouse.Code, a))).ToList();
        var custodyRows = valid.Where(p => p.Row.Custody).ToList();
        var needUnknown = valid.Any(ToUnknown) && !existingByCode.ContainsKey(FullCode(warehouse.Code, SpecialPlaces.UnknownCode));
        var needSorting = valid.Any(ToSort) && !existingByCode.ContainsKey(FullCode(warehouse.Code, SortingCode));

        // ---------- 3. Сотрудники (МОЛ) ----------
        var mols = custodyRows.Select(p => p.Row.Mol!.Trim()).Distinct().ToList();
        var molEmployee = new Dictionary<string, int?>();
        foreach (var mol in mols)
            molEmployee[mol] = PersonNames.Find(staff, mol)?.Id;
        var newEmployees = mols.Where(m => molEmployee[m] is null).ToList();

        var newUnits = valid.Select(p => p.Unit).Distinct(StringComparer.OrdinalIgnoreCase).Where(u => !units.ContainsKey(u)).ToList();
        var newItems = valid.Select(p => p.Sku).Distinct(StringComparer.OrdinalIgnoreCase).Where(s => !items.ContainsKey(s)).ToList();

        if (!apply)
        {
            return Result(false, file, valid, issues, notes, newItems.Count, items, newUnits.Count,
                newNodes.Count + (needUnknown ? 1 : 0) + (needSorting ? 1 : 0), newEmployees.Count, []);
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

        // Места: сверху вниз; тип — по высоте поддерева (ячейка — конечная, выше — полка, стеллаж, зона).
        var failed = new List<string>();
        foreach (var address in neededNodes.OrderBy(a => a.Count(c => c == '.')).ThenBy(a => a, StringComparer.Ordinal))
        {
            var full = FullCode(warehouse.Code, address);
            if (existingByCode.ContainsKey(full))
                continue;
            var parts = address.Split('.');
            int? parentId = parts.Length == 1 ? null : existingByCode[FullCode(warehouse.Code, string.Join('.', parts[..^1]))].Id;
            var type = plan.Types[address];
            var description = parts[^1] == LooseCode && plan.Loose.Contains(address)
                ? "Без ячейки"
                : $"{TypeTitle(type)} {parts[^1]}";
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

        int? unknownId = null;
        if (valid.Any(ToUnknown))
        {
            unknownId = await molService.EnsureUnknownPlaceAsync(warehouse.Id, ct);
            notes.Add($"В «Место не указано» ({FullCode(warehouse.Code, SpecialPlaces.UnknownCode)}): {valid.Count(ToUnknown)} партий других МОЛ без адреса. " +
                      "Выдавать их можно после того, как найдёте и разместите на полку.");
        }

        foreach (var mol in newEmployees)
        {
            var (last, first, middle) = PersonNames.Parse(mol)!.Value;
            var created = await employees.CreateAsync(new SaveEmployeeRequest(null, last, first, middle, null, null, null, IsMol: true), ct);
            molEmployee[mol] = created.Id;
        }

        // Приход: партии с ценой, датой и документом 1С. Не больше 500 строк в документе.
        // Учётное и «вне учёта» — разными документами: признак вне учёта ставится на документ и его партии.
        var documents = new List<long>();
        // Партии других МОЛ — отдельными документами: МОЛ ставится на партии документа.
        foreach (var ((offBook, mol), chunk) in valid
                     .GroupBy(p => (p.Row.OffBook, Mol: p.Row.Custody ? p.Row.Mol!.Trim() : null))
                     .OrderBy(g => g.Key.OffBook).ThenBy(g => g.Key.Mol is null ? 0 : 1).ThenBy(g => g.Key.Mol)
                     .SelectMany(g => g.Chunk(StockService.MaxLines).Select(c => (g.Key, c))))
        {
            var lines = chunk.Select(p => new DocumentLineRequest(
                    items[p.Sku].Id, p.Row.Quantity, null,
                    ToUnknown(p) ? unknownId : ToSort(p) ? sortingId : existingByCode[FullCode(warehouse.Code, string.Join('.', p.Target!))].Id,
                    UnitPrice: p.Price,
                    BatchDate: p.Row.DocumentDate is DateOnly date ? new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null,
                    BatchNote: ToSort(p) ? SortingNote(p.Row) : BatchNote(p.Row)))
                .ToList();
            var doc = await stock.PostDocumentAsync(new CreateDocumentRequest(
                MovementType.Receipt, offBook ? "Ввод остатков вне учёта" : "Ввод остатков из 1С", null, actor.Name,
                (offBook ? "Начальные остатки вне учёта (б/у, неучтённое — в 1С нет)" : "Начальные остатки (импорт из 1С)")
                    + (mol is null ? "" : $"; МОЛ {mol}"),
                lines, PerformedById: actor.EmployeeId, OffBook: offBook, Origin: BatchOrigin.Opening,
                MolId: mol is null ? null : molEmployee[mol]), ct);
            documents.Add(doc.Id);
        }

        return Result(true, file, valid, issues, notes, newItems.Count, items, newUnits.Count,
            newNodes.Count + (needUnknown ? 1 : 0) + (needSorting ? 1 : 0), newEmployees.Count, documents);
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

    /// <summary>Партия без места: на склад, но адреса нет или он не по схеме.</summary>
    private static bool ToSort(Planned p) => !p.Row.Custody && p.Target is null;

    /// <summary>Партия другого МОЛ без адреса — в «Место не указано».</summary>
    private static bool ToUnknown(Planned p) => p.Row.Custody && p.Target is null;

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

    private static string TypeTitle(LocationType type) => type switch
    {
        LocationType.Zone => "Зона",
        LocationType.Rack => "Стеллаж",
        LocationType.Shelf => "Полка",
        _ => "Ячейка"
    };

    private static IEnumerable<LocationNodeDto> Flatten(IEnumerable<LocationNodeDto> nodes) =>
        nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));

    /// <summary>План мест: куда класть товар каждого адреса и тип каждого узла.</summary>
    public sealed record LocationPlan(Dictionary<string, string[]> Targets, Dictionary<string, LocationType> Types, HashSet<string> Loose);

    /// <summary>
    /// Товар хранится только в конечных местах. Если по адресу «2B.3» лежит товар, а есть и «2B.3.1» —
    /// товар «2B.3» кладётся во вложенное место «2B.3.0» («без ячейки»).
    /// Тип узла — по высоте поддерева: конечное — ячейка, над ней полка, стеллаж, зона.
    /// </summary>
    public static LocationPlan PlanLocations(IReadOnlyList<string[]> addresses)
    {
        var all = addresses.Select(a => string.Join('.', a)).Distinct().ToList();
        var nodes = new HashSet<string>(addresses.SelectMany(a => Enumerable.Range(1, a.Length).Select(n => string.Join('.', a.Take(n)))));
        bool HasChildren(string node) => nodes.Any(n => n.StartsWith(node + ".", StringComparison.Ordinal));

        var targets = new Dictionary<string, string[]>();
        var loose = new HashSet<string>();
        foreach (var address in all)
        {
            var parts = address.Split('.');
            if (!HasChildren(address))
            {
                targets[address] = parts;
                continue;
            }
            var code = LooseCode;
            while (nodes.Contains(address + "." + code))
                code += "0";
            targets[address] = [.. parts, code];
            loose.Add(address + "." + code);
        }
        foreach (var t in targets.Values)
            for (var n = 1; n <= t.Length; n++)
                nodes.Add(string.Join('.', t.Take(n)));

        var height = new Dictionary<string, int>();
        foreach (var node in nodes.OrderByDescending(n => n.Count(c => c == '.')))
        {
            var children = nodes.Where(n => n.StartsWith(node + ".", StringComparison.Ordinal) && n.Count(c => c == '.') == node.Count(c => c == '.') + 1);
            height[node] = children.Select(c => height[c] + 1).DefaultIfEmpty(0).Max();
        }
        var types = nodes.ToDictionary(n => n, n => height[n] switch
        {
            0 => LocationType.Cell,
            1 => LocationType.Shelf,
            2 => LocationType.Rack,
            _ => LocationType.Zone
        });
        return new LocationPlan(targets, types, loose);
    }

    private sealed class Planned(OpeningRow row, string sku, string name, string unit, decimal? price, string[]? path)
    {
        public OpeningRow Row { get; } = row;
        public string Sku { get; } = sku;
        public string Name { get; } = name;
        public string Unit { get; } = unit;
        public decimal? Price { get; } = price;
        public string[]? Path { get; } = path;
        public string[]? Target { get; set; }
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
