using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Application.Services;

/// <summary>Особые места склада.</summary>
public static class SpecialPlaces
{
    /// <summary>
    /// «Место не указано» (верхний уровень склада, WH1-UNK): числится за МОЛ, но где лежит — неизвестно.
    /// Выдавать отсюда нельзя — сначала найти и разместить на полку.
    /// </summary>
    public const string UnknownCode = "UNK";

    public const string UnknownName = "Место не указано — найти и разместить";

    public static bool IsUnknown(string? fullCode) =>
        fullCode is not null
        && fullCode.EndsWith(CodeRules.Separator + UnknownCode, StringComparison.OrdinalIgnoreCase)
        && fullCode.Count(c => c == CodeRules.Separator) == 1;
}

public interface IMolService
{
    /// <summary>Сотрудники-МОЛ (основной — первым).</summary>
    Task<IReadOnlyList<MolDto>> ListAsync(CancellationToken ct = default);

    /// <summary>Место «Место не указано» склада (создаётся при первом обращении).</summary>
    Task<int> EnsureUnknownPlaceAsync(int warehouseId, CancellationToken ct = default);

    /// <summary>
    /// Перенос прежнего «подотчёта по 1С»: возврат на склад в «Место не указано», МОЛ партии — сотрудник.
    /// Повторный запуск ничего не делает (подотчёта по 1С уже нет). Возвращает, сколько сотрудников перенесено,
    /// и замечания (что не перенеслось и почему).
    /// </summary>
    Task<(int Employees, IReadOnlyList<string> Problems)> MoveOfficialCustodyAsync(CancellationToken ct = default);
}

/// <summary>МОЛ: пометка партий «за кем числится в 1С» и место «Место не указано».</summary>
public class MolService(
    IStockRepository repo,
    IEmployeeService employees,
    ILocationService locations,
    ISettingsService settings,
    StockService stock) : IMolService
{
    public async Task<IReadOnlyList<MolDto>> ListAsync(CancellationToken ct = default)
    {
        var main = (await settings.GetMainMolAsync(ct)).EmployeeId;
        return (await employees.ListAsync(null, includeInactive: true, ct))
            .Where(e => e.IsMol || e.Id == main)
            .Select(e => new MolDto(e.Id, e.ShortName, e.Id == main))
            .OrderByDescending(m => m.IsMain).ThenBy(m => m.Name)
            .ToList();
    }

    public async Task<int> EnsureUnknownPlaceAsync(int warehouseId, CancellationToken ct = default)
    {
        var tree = await locations.GetLocationTreeAsync(warehouseId, ct);
        if (tree.FirstOrDefault(n => string.Equals(n.Code, SpecialPlaces.UnknownCode, StringComparison.OrdinalIgnoreCase)) is { } existing)
            return existing.Id;
        var created = await locations.AddLocationAsync(warehouseId,
            new CreateLocationRequest(null, LocationType.Cell, SpecialPlaces.UnknownCode, SpecialPlaces.UnknownName), ct);
        return created.Id;
    }

    public async Task<(int Employees, IReadOnlyList<string> Problems)> MoveOfficialCustodyAsync(CancellationToken ct = default)
    {
        var official = await repo.ListOfficialCustodyAsync(ct);
        if (official.Count == 0)
            return (0, []);

        var warehouse = (await locations.ListWarehousesAsync(ct)).OrderBy(w => w.Id).FirstOrDefault();
        if (warehouse is null)
            return (0, ["Подотчёт по 1С не перенесён: нет ни одного склада."]);
        var unknown = await EnsureUnknownPlaceAsync(warehouse.Id, ct);

        var problems = new List<string>();
        var moved = 0;
        foreach (var group in official.GroupBy(b => b.EmployeeId))
        {
            var name = group.First().Employee.ShortName;
            try
            {
                foreach (var chunk in group.Chunk(StockService.MaxLines))
                {
                    // 1. Вернуть на склад в «Место не указано» той же партией.
                    await stock.StageAsync(new CreateDocumentRequest(
                            MovementType.Return, null, null, "Перенос подотчёта по 1С",
                            $"Подотчёт по 1С ({name}) перенесён на склад: место не указано, МОЛ — {name}",
                            chunk.Select(b => new DocumentLineRequest(b.ItemId, b.Quantity, null, unknown, b.BatchId)).ToList(),
                            ReceivedById: group.Key, Custody: CustodyKind.Official),
                        exceptActId: null, ct);
                    await repo.SaveChangesAsync(ct);

                    // 2. Пометить: числится за этим МОЛ.
                    await stock.StageMolChangeAsync(new ChangeMolRequest(
                            chunk.Select(b => new MolChangeLine(unknown, b.BatchId, b.Quantity, group.Key)).ToList(),
                            $"Перенос подотчёта по 1С: МОЛ — {name}"),
                        null, "Перенос подотчёта по 1С", ct);
                    await repo.SaveChangesAsync(ct);
                }
                moved++;
            }
            catch (DomainException e)
            {
                problems.Add($"{name}: {e.Message}");
            }
        }
        return (moved, problems);
    }
}
