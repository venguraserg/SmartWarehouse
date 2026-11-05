using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;

namespace SmartWarehouse.Application.Services;

public interface IReconcileService
{
    /// <summary>
    /// Сверка остатков приложения с «Сальдовыми ведомостями ТМЦ» 1С.
    /// <paramref name="warehouseMol"/> — МОЛ основного склада (его остатки сравниваются со складом приложения);
    /// null — тот, у кого в 1С больше всего стоимость. Остальные МОЛ сравниваются с подотчётом сотрудников.
    /// </summary>
    Task<ReconcileResultDto> ReconcileAsync(IReadOnlyList<SaldoSheet> sheets, string? warehouseMol, CancellationToken ct = default);
}

public class ReconcileService(IStockRepository stock, IEmployeeService employees) : IReconcileService
{
    public const string WarehousePlace = "Склад";

    public async Task<ReconcileResultDto> ReconcileAsync(IReadOnlyList<SaldoSheet> sheets, string? warehouseMol, CancellationToken ct = default)
    {
        var notes = new List<string>();
        var batches = sheets.SelectMany(s => s.Batches).ToList();
        var asOf = sheets.Select(s => s.AsOf).Where(a => a is not null).Distinct().ToList();
        if (asOf.Count > 1)
            notes.Add("В файлах разные даты остатков: " + string.Join(", ", asOf) + ".");

        // МОЛ: склад — указанный или с наибольшей стоимостью; остальные — сотрудники (подотчёт).
        var molValues = batches.GroupBy(b => b.Mol)
            .Select(g => (Mol: g.Key, Count: g.Count(), Value: g.Sum(b => b.Value ?? 0)))
            .OrderByDescending(m => m.Value)
            .ToList();
        var requested = warehouseMol?.Trim();
        warehouseMol = molValues.Select(m => m.Mol).FirstOrDefault(m => string.Equals(m, requested, StringComparison.OrdinalIgnoreCase))
                       ?? molValues.Select(m => m.Mol).FirstOrDefault();

        var staff = await employees.ListAsync(null, includeInactive: true, ct);
        var molPlace = new Dictionary<string, (string? Place, int? EmployeeId)>();
        var mols = new List<ReconcileMolDto>();
        foreach (var m in molValues)
        {
            (string? Place, int? EmployeeId) place = m.Mol == warehouseMol
                ? (WarehousePlace, null)
                : PersonNames.Find(staff, m.Mol) is { } e ? (e.FullName, e.Id) : (null, null);
            molPlace[m.Mol] = place;
            mols.Add(new ReconcileMolDto(m.Mol, place.Place, place.EmployeeId, m.Count, decimal.Round(m.Value, 2)));
            if (place.Place is null)
                notes.Add($"МОЛ «{m.Mol}» не найден среди сотрудников приложения — его позиции показаны как «есть только в 1С».");
        }

        var warehouse = (await stock.ListWarehouseTotalsAsync(ct)).ToDictionary(t => Key(t.Sku));
        var custody = (await stock.ListCustodyTotalsAsync(ct)).ToLookup(t => t.EmployeeId);

        var rows = new List<ReconcileRowDto>();
        var seen = new HashSet<(string Mol, string Code)>();
        foreach (var g in batches.GroupBy(b => (b.Mol, Code: Key(b.Code))))
        {
            seen.Add(g.Key);
            var first = g.First();
            var qty1C = g.Sum(b => b.Quantity);
            var value1C = g.All(b => b.Value is null) ? (decimal?)null : decimal.Round(g.Sum(b => b.Value ?? 0), 2);
            var (place, employeeId) = molPlace[g.Key.Mol];

            (int ItemId, string Name, string Unit, decimal Qty, decimal Value, bool Incomplete)? app = null;
            if (place == WarehousePlace && warehouse.TryGetValue(g.Key.Code, out var w))
                app = (w.ItemId, w.Name, w.Unit, w.Quantity, w.Value, w.ValueIncomplete);
            else if (employeeId is int eid && custody[eid].FirstOrDefault(c => Key(c.Sku) == g.Key.Code) is { } c)
                app = (c.ItemId, c.Name, c.Unit, c.Quantity, c.Value, c.ValueIncomplete);

            var qtyApp = app?.Qty ?? 0;
            var status = app is null || qtyApp == 0
                ? ReconcileStatus.OnlyIn1C
                : qtyApp != qty1C
                    ? ReconcileStatus.QtyDiffers
                    : value1C is decimal v && (app.Value.Incomplete || Math.Abs(app.Value.Value - v) > 0.01m)
                        ? ReconcileStatus.ValueDiffers
                        : ReconcileStatus.Match;
            rows.Add(new ReconcileRowDto(status, first.Code, first.Name, app?.Unit ?? first.Unit, g.Key.Mol,
                place ?? "не найден в приложении", app?.ItemId, qty1C, qtyApp, value1C, app?.Value));
        }

        // Есть в приложении, нет в 1С — у склада и у сотрудников, чьи ведомости загружены.
        foreach (var w in warehouse.Values.Where(w => w.Quantity != 0 && !seen.Contains((warehouseMol!, Key(w.Sku)))))
            rows.Add(new ReconcileRowDto(ReconcileStatus.OnlyInApp, w.Sku, w.Name, w.Unit, warehouseMol ?? "", WarehousePlace,
                w.ItemId, 0, w.Quantity, null, w.Value));
        foreach (var (mol, (place, employeeId)) in molPlace.Where(m => m.Value.EmployeeId is not null))
            foreach (var c in custody[employeeId!.Value].Where(c => c.Quantity != 0 && !seen.Contains((mol, Key(c.Sku)))))
                rows.Add(new ReconcileRowDto(ReconcileStatus.OnlyInApp, c.Sku, c.Name, c.Unit, mol, place!,
                    c.ItemId, 0, c.Quantity, null, c.Value));

        var otherCustody = custody.Select(c => c.Key).Except(molPlace.Values.Select(v => v.EmployeeId).OfType<int>()).Count();
        if (otherCustody > 0)
            notes.Add($"Подотчёт ещё {otherCustody} сотрудников не сверялся — их ведомостей 1С нет среди загруженных.");

        var order = new[] { ReconcileStatus.QtyDiffers, ReconcileStatus.OnlyIn1C, ReconcileStatus.OnlyInApp, ReconcileStatus.ValueDiffers, ReconcileStatus.Match };
        rows = rows
            .OrderBy(r => Array.IndexOf(order, r.Status))
            .ThenBy(r => r.Place == WarehousePlace ? 0 : 1)
            .ThenBy(r => r.Mol)
            .ThenBy(r => r.Code, StringComparer.Ordinal)
            .ToList();
        return new ReconcileResultDto(asOf.FirstOrDefault(), warehouseMol, mols, rows, notes);
    }

    /// <summary>Код для сравнения: 0000032353 и 32353 — один код.</summary>
    public static string Key(string code)
    {
        var c = code.Trim().ToUpperInvariant();
        return c.All(char.IsAsciiDigit) ? c.TrimStart('0') is { Length: > 0 } t ? t : "0" : c;
    }
}
