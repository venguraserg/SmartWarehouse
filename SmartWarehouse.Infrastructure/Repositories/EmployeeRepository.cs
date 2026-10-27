using Microsoft.EntityFrameworkCore;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Infrastructure.Data;

namespace SmartWarehouse.Infrastructure.Repositories;

public class EmployeeRepository(WarehouseDbContext db) : IEmployeeRepository
{
    public async Task<IReadOnlyList<Employee>> ListAsync(string? search, bool includeInactive, CancellationToken ct = default)
    {
        var query = db.Employees.AsNoTracking();

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
        db.Employees.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<bool> PersonnelNumberExistsAsync(string number, int? exceptId, CancellationToken ct = default) =>
        db.Employees.AnyAsync(x => x.PersonnelNumber == number && x.Id != exceptId, ct);

    public void Add(Employee employee) => db.Employees.Add(employee);

    public async Task<IReadOnlyList<StockMovement>> GetIssuesAsync(int employeeId, int take, CancellationToken ct = default) =>
        await db.Movements
            .AsNoTracking()
            .Include(x => x.Document)
            .Include(x => x.Item).ThenInclude(i => i.Unit)
            .Include(x => x.FromLocation)
            .Where(x => x.Type == MovementType.Issue && x.Document.ReceivedById == employeeId)
            .OrderByDescending(x => x.OccurredAt)
            .ThenByDescending(x => x.Id)
            .Take(take)
            .ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
