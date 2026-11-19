using Microsoft.EntityFrameworkCore;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Infrastructure.Data;

namespace SmartWarehouse.Infrastructure.Repositories;

public class IssueRequestRepository(WarehouseDbContext db) : IIssueRequestRepository
{
    public void Add(IssueRequest request) => db.IssueRequests.Add(request);

    public Task<IssueRequest?> GetAsync(int id, CancellationToken ct = default) =>
        db.IssueRequests
            .Include(x => x.Employee)
            .Include(x => x.Lines).ThenInclude(l => l.Item).ThenInclude(i => i.Unit)
            .Include(x => x.Document).ThenInclude(d => d!.Lines)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<IssueRequest>> ListAsync(
        IReadOnlyCollection<int>? employeeIds, IssueRequestStatus? status, int take, CancellationToken ct = default)
    {
        var query = db.IssueRequests.AsNoTracking();
        if (employeeIds is not null)
        {
            var ids = employeeIds.ToList();
            query = query.Where(x => ids.Contains(x.EmployeeId));
        }
        if (status is not null)
            query = query.Where(x => x.Status == status);

        var list = await query
            .Include(x => x.Employee)
            .Include(x => x.Lines)
            .Include(x => x.Document)
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .AsSplitQuery()
            .ToListAsync(ct);

        // Открытые — сверху, в порядке поступления (кого обслужить первым); остальные — новые сверху.
        return list
            .OrderBy(x => x.Status == IssueRequestStatus.Open ? 0 : 1)
            .ThenBy(x => x.Status == IssueRequestStatus.Open ? x.CreatedAt.UtcTicks : -x.CreatedAt.UtcTicks)
            .ToList();
    }

    public Task<int> CountOpenAsync(CancellationToken ct = default) =>
        db.IssueRequests.CountAsync(x => x.Status == IssueRequestStatus.Open, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
