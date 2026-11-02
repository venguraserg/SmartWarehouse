using Microsoft.EntityFrameworkCore;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Infrastructure.Data;

namespace SmartWarehouse.Infrastructure.Repositories;

public class SettingsRepository(WarehouseDbContext db) : ISettingsRepository
{
    public Task<AppSetting?> GetAsync(string key, CancellationToken ct = default) =>
        db.Settings.FirstOrDefaultAsync(x => x.Key == key, ct);

    public void Add(AppSetting setting) => db.Settings.Add(setting);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
