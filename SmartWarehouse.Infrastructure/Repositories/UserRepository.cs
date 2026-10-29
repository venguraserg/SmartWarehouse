using Microsoft.EntityFrameworkCore;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Infrastructure.Data;

namespace SmartWarehouse.Infrastructure.Repositories;

public class UserRepository(WarehouseDbContext db) : IUserRepository
{
    public Task<bool> AnyAsync(CancellationToken ct = default) => db.Users.AnyAsync(ct);

    public Task<UserAccount?> GetByLoginAsync(string login, CancellationToken ct = default) =>
        db.Users.Include(x => x.Employee).FirstOrDefaultAsync(x => x.Login == login, ct);

    public Task<UserAccount?> GetAsync(int id, CancellationToken ct = default) =>
        db.Users.Include(x => x.Employee).FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken ct = default) =>
        await db.Users.AsNoTracking().Include(x => x.Employee).OrderBy(x => x.Login).ToListAsync(ct);

    public Task<bool> LoginExistsAsync(string login, CancellationToken ct = default) =>
        db.Users.AnyAsync(x => x.Login == login, ct);

    public Task<bool> EmployeeHasAccountAsync(int employeeId, int? exceptUserId, CancellationToken ct = default) =>
        db.Users.AnyAsync(x => x.EmployeeId == employeeId && x.Id != exceptUserId, ct);

    public Task<int> CountActiveAdminsAsync(int? exceptUserId, CancellationToken ct = default) =>
        db.Users.CountAsync(x => x.IsActive && x.Roles.HasFlag(UserRole.Admin) && x.Id != exceptUserId, ct);

    public Task<Employee?> GetEmployeeAsync(int employeeId, CancellationToken ct = default) =>
        db.Employees.FirstOrDefaultAsync(x => x.Id == employeeId, ct);

    public void Add(UserAccount user) => db.Users.Add(user);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
