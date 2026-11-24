using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;

namespace SmartWarehouse.Application.Services;

public interface IMolService
{
    /// <summary>МОЛ склада — кладовщики (сотрудники с активной учётной записью и ролью «Кладовщик»).</summary>
    Task<IReadOnlyList<MolDto>> ListAsync(CancellationToken ct = default);
}

/// <summary>
/// МОЛ склада. За кем числится партия в 1С — пометка партии; МОЛ может быть только кладовщик.
/// На складе кладовщиков-МОЛ может быть несколько; при замене кладовщика партии передаются накладной на перемещение.
/// </summary>
public class MolService(IStockRepository repo, IEmployeeService employees) : IMolService
{
    public async Task<IReadOnlyList<MolDto>> ListAsync(CancellationToken ct = default)
    {
        var ids = (await repo.ListStorekeeperIdsAsync(ct)).ToHashSet();
        return (await employees.ListAsync(null, includeInactive: true, ct))
            .Where(e => ids.Contains(e.Id))
            .Select(e => new MolDto(e.Id, e.ShortName))
            .OrderBy(m => m.Name)
            .ToList();
    }
}
