using Microsoft.EntityFrameworkCore;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Infrastructure.Data;

namespace SmartWarehouse.Infrastructure.Repositories;

public class EmployeeRepository(WarehouseDbContext db) : IEmployeeRepository
{
    public async Task<IReadOnlyList<Employee>> ListAsync(string? search, bool includeInactive, CancellationToken ct = default)
    {
        var query = db.Employees.AsNoTracking().Include(x => x.Manager).AsQueryable();

        if (!includeInactive)
            query = query.Where(x => x.IsActive);

        if (search is not null)
        {
            var pattern = $"%{search}%";
            query = query.Where(x =>
                EF.Functions.ILike(x.LastName, pattern)
                || EF.Functions.ILike(x.FirstName, pattern)
                || (x.MiddleName != null && EF.Functions.ILike(x.MiddleName, pattern))
                || (x.PersonnelNumber != null && EF.Functions.ILike(x.PersonnelNumber, pattern))
                || (x.Position != null && EF.Functions.ILike(x.Position, pattern))
                || (x.Department != null && EF.Functions.ILike(x.Department, pattern)));
        }

        return await query
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ThenBy(x => x.MiddleName)
            .Take(1000)
            .ToListAsync(ct);
    }

    public Task<Employee?> GetAsync(int id, CancellationToken ct = default) =>
        db.Employees.Include(x => x.Manager).FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<(int Id, int? ManagerId)>> ListHierarchyAsync(CancellationToken ct = default) =>
        (await db.Employees.AsNoTracking().Select(x => new { x.Id, x.ManagerId }).ToListAsync(ct))
            .Select(x => (x.Id, x.ManagerId))
            .ToList();

    public Task<bool> PersonnelNumberExistsAsync(string number, int? exceptId, CancellationToken ct = default) =>
        db.Employees.AnyAsync(x => x.PersonnelNumber == number && x.Id != exceptId, ct);

    public void Add(Employee employee) => db.Employees.Add(employee);

    public async Task<IReadOnlyList<StockMovement>> GetHistoryAsync(int employeeId, int take, CancellationToken ct = default) =>
        await db.Movements
            .AsNoTracking()
            .Include(x => x.Document)
            .Include(x => x.Item).ThenInclude(i => i.Unit)
            .Include(x => x.FromLocation)
            .Include(x => x.ToLocation)
            .Where(x => x.FromEmployeeId == employeeId || x.ToEmployeeId == employeeId)
            .OrderByDescending(x => x.OccurredAt)
            .ThenByDescending(x => x.Id)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<EmployeeBalance>> ListCustodyAsync(
        int? employeeId, int? itemId, string? search, IReadOnlyCollection<int>? onlyEmployeeIds, CancellationToken ct = default)
    {
        var query = db.EmployeeBalances
            .AsNoTracking()
            .Include(x => x.Employee)
            .Include(x => x.Item).ThenInclude(i => i.Unit)
            .Where(x => x.Quantity > 0);

        if (employeeId is not null)
            query = query.Where(x => x.EmployeeId == employeeId);
        if (onlyEmployeeIds is not null)
        {
            var allowed = onlyEmployeeIds.ToList();
            query = query.Where(x => allowed.Contains(x.EmployeeId));
        }
        if (itemId is not null)
            query = query.Where(x => x.ItemId == itemId);
        if (search is not null)
        {
            var pattern = $"%{search}%";
            query = query.Where(x =>
                EF.Functions.ILike(x.Item.Name, pattern)
                || EF.Functions.ILike(x.Item.Sku, pattern)
                || EF.Functions.ILike(x.Employee.LastName, pattern)
                || (x.Employee.PersonnelNumber != null && EF.Functions.ILike(x.Employee.PersonnelNumber, pattern)));
        }

        return await query.Take(5000).ToListAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
