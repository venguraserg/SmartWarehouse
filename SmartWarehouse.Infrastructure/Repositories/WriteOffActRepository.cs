using Microsoft.EntityFrameworkCore;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Infrastructure.Data;

namespace SmartWarehouse.Infrastructure.Repositories;

public class WriteOffActRepository(WarehouseDbContext db) : IWriteOffActRepository
{
    public Task<WriteOffAct?> GetAsync(int id, CancellationToken ct = default) =>
        db.WriteOffActs
            .Include(x => x.Employee).ThenInclude(e => e.Manager)
            .Include(x => x.Lines).ThenInclude(l => l.Item).ThenInclude(i => i.Unit)
            .Include(x => x.Lines).ThenInclude(l => l.Equipment)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<ActHeader>> ListAsync(ActFilter filter, CancellationToken ct = default)
    {
        var query = db.WriteOffActs.AsNoTracking().AsQueryable();

        if (filter.EmployeeIds is not null)
        {
            var ids = filter.EmployeeIds.ToList();
            query = query.Where(x => ids.Contains(x.EmployeeId));
        }
        if (filter.ApproverId is int approver)
            query = ForApprover(query, approver);
        if (filter.Status is ActStatus status)
            query = query.Where(x => x.Status == status);
        if (filter.EmployeeId is int employeeId)
            query = query.Where(x => x.EmployeeId == employeeId);

        var rows = await query
            .OrderByDescending(x => x.UpdatedAt)
            .ThenByDescending(x => x.Id)
            .Take(filter.Take)
            .Select(x => new { Act = x, x.Employee, x.Employee.Manager, LineCount = x.Lines.Count })
            .ToListAsync(ct);

        return rows
            .Select(r =>
            {
                r.Act.Employee = r.Employee;
                r.Employee.Manager = r.Manager;
                return new ActHeader(r.Act, r.LineCount);
            })
            .ToList();
    }

    public Task<int> CountToApproveAsync(int approverId, CancellationToken ct = default) =>
        ForApprover(db.WriteOffActs.Where(x => x.Status == ActStatus.Pending), approverId).CountAsync(ct);

    /// <summary>Акты, которые утверждает сотрудник: его подчинённых, а если у автора руководителя нет — его собственные.</summary>
    private static IQueryable<WriteOffAct> ForApprover(IQueryable<WriteOffAct> query, int approverId) =>
        query.Where(x => x.Employee.ManagerId == approverId || (x.Employee.ManagerId == null && x.EmployeeId == approverId));

    public void Add(WriteOffAct act) => db.WriteOffActs.Add(act);

    public void Remove(WriteOffAct act) => db.WriteOffActs.Remove(act);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
