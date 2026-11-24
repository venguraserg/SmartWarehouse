using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;

namespace SmartWarehouse.Application.Services;

public interface IReconcileService
{
    /// <summary>
    /// Сверка остатков приложения с «Сальдовыми ведомостями ТМЦ» 1С — по каждому кладовщику-МОЛ: позиция × МОЛ × количество.
    /// Ведомости других МОЛ (подотчёт специалистов) не сверяются. <paramref name="warehouseMol"/> — за кем считать партии
    /// без пометки МОЛ (старые данные); null — кладовщик с наибольшей стоимостью в 1С.
    /// </summary>
    Task<ReconcileResultDto> ReconcileAsync(IReadOnlyList<SaldoSheet> sheets, string? warehouseMol, CancellationToken ct = default);
}

public class ReconcileService(IStockRepository stock, IEmployeeService employees, IMolService? molService = null) : IReconcileService
{
    /// <summary>Подпись МОЛ-кладовщика в строках сверки: его партии на складе плюс выданное «в работу».</summary>
    public const string WarehousePlace = "Склад";

    public async Task<ReconcileResultDto> ReconcileAsync(IReadOnlyList<SaldoSheet> sheets, string? warehouseMol, CancellationToken ct = default)
    {
        var notes = new List<string>();
        var batches = sheets.SelectMany(s => s.Batches).ToList();
        var asOf = sheets.Select(s => s.AsOf).Where(a => a is not null).Distinct().ToList();
        if (asOf.Count > 1)
            notes.Add("В файлах разные даты остатков: " + string.Join(", ", asOf) + ".");

        var staff = await employees.ListAsync(null, includeInactive: true, ct);
        // Кладовщики-МОЛ; без сервиса МОЛ (тесты) — все сотрудники.
        var storekeeperIds = molService is null ? staff.Select(e => e.Id).ToHashSet() : (await molService.ListAsync(ct)).Select(m => m.EmployeeId).ToHashSet();
        var storekeepers = staff.Where(e => storekeeperIds.Contains(e.Id)).ToList();

        var molValues = batches.GroupBy(b => b.Mol)
            .Select(g => (Mol: g.Key, Count: g.Count(), Value: g.Sum(b => b.Value ?? 0)))
            .OrderByDescending(m => m.Value)
            .ToList();

        // МОЛ из 1С → кладовщик приложения; не кладовщик — ведомость не сверяется (подотчёт специалистов — не склад).
        var molKey = new Dictionary<string, (bool Found, int? MolId, string Place)>();
        var mols = new List<ReconcileMolDto>();
        foreach (var m in molValues)
        {
            (bool Found, int? MolId, string Place) key = PersonNames.Find(storekeepers, m.Mol) is { } e
                ? (true, e.Id, $"{WarehousePlace} ({e.ShortName})")
                : (false, null, "");
            molKey[m.Mol] = key;
            mols.Add(new ReconcileMolDto(m.Mol, key.Found ? key.Place : null, key.MolId, m.Count, decimal.Round(m.Value, 2)));
            if (!key.Found)
                notes.Add($"МОЛ «{m.Mol}» — не кладовщик склада: его ведомость не сверяется" +
                          (PersonNames.Find(staff, m.Mol) is null ? "." : " (сотрудник есть, но без учётной записи с ролью «Кладовщик»)."));
        }

        // Партии без пометки МОЛ (старые данные) — за указанным кладовщиком, иначе за тем, у кого в 1С больше стоимость.
        var requested = warehouseMol?.Trim();
        warehouseMol = molValues.Select(m => m.Mol).FirstOrDefault(m => molKey[m].Found && string.Equals(m, requested, StringComparison.OrdinalIgnoreCase))
                       ?? molValues.Select(m => m.Mol).FirstOrDefault(m => molKey[m].Found);
        var unlabeledTo = warehouseMol is null ? null : molKey[warehouseMol].MolId;

        var loaded = molKey.Values.Where(k => k.Found).Select(k => k.MolId).ToHashSet();
        string PlaceOf(int? molId) => molKey.Values.FirstOrDefault(k => k.Found && k.MolId == molId).Place ?? "";
        string MolOf(int? molId) => molKey.FirstOrDefault(k => k.Value.Found && k.Value.MolId == molId).Key ?? "";

        var totals = (await stock.ListMolTotalsAsync(ct))
            .Select(t => t.MolId is null && unlabeledTo is not null ? t with { MolId = unlabeledTo } : t)
            .GroupBy(t => (t.MolId, Code: Key(t.Sku)))
            .ToDictionary(g => g.Key, g => g.Count() == 1 ? g.First() : g.First() with
            {
                Quantity = g.Sum(t => t.Quantity), Value = g.Sum(t => t.Value), ValueIncomplete = g.Any(t => t.ValueIncomplete)
            });
        var rows = new List<ReconcileRowDto>();
        var seen = new HashSet<(int? MolId, string Code)>();
        var qty1C = new Dictionary<(int? MolId, string Code), decimal>();
        foreach (var g in batches.Where(b => molKey[b.Mol].Found).GroupBy(b => (b.Mol, Code: Key(b.Code))))
        {
            var first = g.First();
            var quantity = g.Sum(b => b.Quantity);
            var value1C = g.All(b => b.Value is null) ? (decimal?)null : decimal.Round(g.Sum(b => b.Value ?? 0), 2);
            var key = molKey[g.Key.Mol];

            MolTotal? app = null;
            if (key.Found)
            {
                // Два МОЛ из 1С могут указывать на одну пометку (основной МОЛ под двумя написаниями) — сравнивается один раз.
                if (!seen.Add((key.MolId, g.Key.Code)))
                    notes.Add($"{first.Code}: МОЛ «{g.Key.Mol}» совпал с другим МОЛ из файлов — проверьте вручную.");
                qty1C[(key.MolId, g.Key.Code)] = qty1C.GetValueOrDefault((key.MolId, g.Key.Code)) + quantity;
                totals.TryGetValue((key.MolId, g.Key.Code), out app);
            }

            var qtyApp = app?.Quantity ?? 0;
            var status = app is null || qtyApp == 0
                ? ReconcileStatus.OnlyIn1C
                : qtyApp != quantity
                    ? ReconcileStatus.QtyDiffers
                    : value1C is decimal v && (app.ValueIncomplete || Math.Abs(app.Value - v) > 0.01m)
                        ? ReconcileStatus.ValueDiffers
                        : ReconcileStatus.Match;
            rows.Add(new ReconcileRowDto(status, first.Code, first.Name, app?.Unit ?? first.Unit, g.Key.Mol,
                key.Found ? key.Place : "не кладовщик — не сверяется", app?.ItemId, quantity, qtyApp, value1C, app?.Value));
        }

        // Есть в приложении, нет в 1С — у МОЛ, чьи ведомости загружены.
        foreach (var t in totals.Values.Where(t => t.Quantity != 0 && loaded.Contains(t.MolId) && !seen.Contains((t.MolId, Key(t.Sku)))))
            rows.Add(new ReconcileRowDto(ReconcileStatus.OnlyInApp, t.Sku, t.Name, t.Unit, MolOf(t.MolId), PlaceOf(t.MolId),
                t.ItemId, 0, t.Quantity, null, t.Value));

        var notLoaded = totals.Values.Where(t => t.Quantity != 0 && !loaded.Contains(t.MolId)).Select(t => t.MolId).Distinct().Count();
        if (notLoaded > 0)
            notes.Add($"Ещё {notLoaded} кладовщиков-МОЛ не сверялись — их ведомостей 1С нет среди загруженных.");

        var changes = await SuggestMolChangesAsync(totals, qty1C, loaded, unlabeledTo, PlaceOf, ct);
        if (changes.Count > 0)
            notes.Add($"Позиций, где МОЛ в приложении расходится с 1С: {changes.Count} — их можно «принять МОЛ из 1С».");

        var order = new[] { ReconcileStatus.QtyDiffers, ReconcileStatus.OnlyIn1C, ReconcileStatus.OnlyInApp, ReconcileStatus.ValueDiffers, ReconcileStatus.Match };
        rows = rows
            .OrderBy(r => Array.IndexOf(order, r.Status))
            .ThenBy(r => r.Place == WarehousePlace ? 0 : 1)
            .ThenBy(r => r.Mol)
            .ThenBy(r => r.Code, StringComparer.Ordinal)
            .ToList();
        return new ReconcileResultDto(asOf.FirstOrDefault(), warehouseMol, mols, rows, notes, changes);
    }

    /// <summary>
    /// «Принять МОЛ из 1С»: по позиции у одного кладовщика в приложении больше, чем в 1С, у другого — меньше.
    /// Сменить пометку можно у того, что лежит в местах хранения (старые партии первыми). Выданное «в работу» не трогается.
    /// </summary>
    private async Task<List<ReconcileMolChangeDto>> SuggestMolChangesAsync(
        Dictionary<(int? MolId, string Code), MolTotal> totals,
        Dictionary<(int? MolId, string Code), decimal> qty1C,
        HashSet<int?> loaded,
        int? unlabeledTo,
        Func<int?, string> placeOf,
        CancellationToken ct)
    {
        var result = new List<ReconcileMolChangeDto>();
        var codes = qty1C.Keys.Select(k => k.Code).Concat(totals.Keys.Where(k => loaded.Contains(k.MolId)).Select(k => k.Code)).Distinct().ToList();
        var plan = new List<(string Code, int? From, int? To, decimal Quantity, MolTotal Item)>();
        foreach (var code in codes)
        {
            decimal Diff(int? mol) => (totals.TryGetValue((mol, code), out var t) ? t.Quantity : 0) - qty1C.GetValueOrDefault((mol, code));
            var surplus = loaded.Select(m => (Mol: m, Qty: Diff(m))).Where(x => x.Qty > 0).ToList();
            var deficit = loaded.Select(m => (Mol: m, Qty: -Diff(m))).Where(x => x.Qty > 0).ToList();
            foreach (var d in deficit)
            {
                var need = d.Qty;
                for (var i = 0; i < surplus.Count && need > 0; i++)
                {
                    var take = Math.Min(need, surplus[i].Qty);
                    if (take <= 0)
                        continue;
                    plan.Add((code, surplus[i].Mol, d.Mol, take, totals[(surplus[i].Mol, code)]));
                    surplus[i] = (surplus[i].Mol, surplus[i].Qty - take);
                    need -= take;
                }
            }
        }
        if (plan.Count == 0)
            return result;

        var sources = (await stock.ListMolSourcesAsync(plan.Select(p => p.Item.ItemId).Distinct().ToList(), ct))
            .Where(b => b.Batch is not null)
            .ToList();
        var used = new Dictionary<(int LocationId, int BatchId), decimal>();
        foreach (var p in plan)
        {
            var candidates = sources
                .Where(b => b.ItemId == p.Item.ItemId && (b.Batch.MolId ?? unlabeledTo) == p.From)
                .OrderBy(b => b.Batch.ReceivedAt).ThenBy(b => b.BatchId)
                .ToList();
            var left = p.Quantity;
            var list = new List<MolSourceDto>();
            foreach (var b in candidates)
            {
                var available = b.Quantity - used.GetValueOrDefault((b.LocationId, b.BatchId));
                if (available <= 0)
                    continue;
                var proposed = Math.Min(available, left);
                left -= proposed;
                used[(b.LocationId, b.BatchId)] = used.GetValueOrDefault((b.LocationId, b.BatchId)) + proposed;
                list.Add(new MolSourceDto(b.LocationId, b.Location?.FullCode ?? "", b.Location?.Name, b.BatchId, b.Batch.ReceivedAt,
                    available, proposed));
            }
            var suggested = p.Quantity - left;
            if (suggested <= 0)
                continue;   // всё «в работе» — пометку там не меняем
            result.Add(new ReconcileMolChangeDto(p.Item.ItemId, p.Item.Sku, p.Item.Name, p.Item.Unit,
                p.From!.Value, placeOf(p.From), p.To!.Value, placeOf(p.To), suggested, list));
        }
        return result.OrderBy(r => r.ToMol).ThenBy(r => r.Name).ToList();
    }

    /// <summary>Код для сравнения: 0000032353 и 32353 — один код.</summary>
    public static string Key(string code)
    {
        var c = code.Trim().ToUpperInvariant();
        return c.All(char.IsAsciiDigit) ? c.TrimStart('0') is { Length: > 0 } t ? t : "0" : c;
    }
}
