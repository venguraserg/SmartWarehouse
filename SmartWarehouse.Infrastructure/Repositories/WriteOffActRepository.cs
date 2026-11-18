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
            .Include(x => x.Lines).ThenInclude(l => l.Batch)
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
        if (filter.ApproverId is not null || filter.ReportsToConfirm)
            query = ToDecide(query, filter.ApproverId, filter.ReportsToConfirm);
        if (filter.Kind is ActKind kind)
            query = query.Where(x => x.Kind == kind);
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

    public Task<int> CountToApproveAsync(int? approverId, bool reportsToConfirm, CancellationToken ct = default) =>
        ToDecide(db.WriteOffActs, approverId, reportsToConfirm).CountAsync(ct);

    /// <summary>
    /// Ждут решения: акты на утверждении, которые утверждает сотрудник (его подчинённых, а если у автора
    /// руководителя нет — его собственные), и — для кладовщика — отчёты об использовании на подтверждении.
    /// </summary>
    private static IQueryable<WriteOffAct> ToDecide(IQueryable<WriteOffAct> query, int? approverId, bool reportsToConfirm) =>
        query.Where(x => x.Status == ActStatus.Pending
            && ((x.Kind == ActKind.WriteOff && approverId != null
                    && (x.Employee.ManagerId == approverId || (x.Employee.ManagerId == null && x.EmployeeId == approverId)))
                || (reportsToConfirm && x.Kind == ActKind.UsageReport)));

    public void Add(WriteOffAct act) => db.WriteOffActs.Add(act);

    public void Remove(WriteOffAct act) => db.WriteOffActs.Remove(act);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
