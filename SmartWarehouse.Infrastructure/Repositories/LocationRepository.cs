using Microsoft.EntityFrameworkCore;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Infrastructure.Data;

namespace SmartWarehouse.Infrastructure.Repositories;

public class LocationRepository(WarehouseDbContext db) : ILocationRepository
{
    public Task<Warehouse?> GetWarehouseAsync(int id, CancellationToken ct = default) =>
        db.Warehouses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Warehouse>> ListWarehousesAsync(CancellationToken ct = default) =>
        await db.Warehouses.AsNoTracking().OrderBy(x => x.Code).ToListAsync(ct);

    public Task<bool> WarehouseCodeExistsAsync(string code, CancellationToken ct = default) =>
        db.Warehouses.AnyAsync(x => x.Code == code, ct);

    public void AddWarehouse(Warehouse warehouse) => db.Warehouses.Add(warehouse);

    public Task<StorageLocation?> GetLocationAsync(int id, CancellationToken ct = default) =>
        db.Locations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<StorageLocation?> GetLocationByFullCodeAsync(string fullCode, CancellationToken ct = default) =>
        db.Locations.AsNoTracking().FirstOrDefaultAsync(x => x.FullCode == fullCode, ct);

    public async Task<IReadOnlyList<StorageLocation>> GetLocationsAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default)
    {
        var list = ids.ToList();
        return await db.Locations.AsNoTracking().Include(x => x.Warehouse).Where(x => list.Contains(x.Id)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<StorageLocation>> ListLocationsAsync(int warehouseId, CancellationToken ct = default) =>
        await db.Locations.AsNoTracking().Where(x => x.WarehouseId == warehouseId).ToListAsync(ct);

    public Task<bool> FullCodeExistsAsync(string fullCode, CancellationToken ct = default) =>
        db.Locations.AnyAsync(x => x.FullCode == fullCode, ct);

    public Task<bool> LocationHasStockAsync(int locationId, CancellationToken ct = default) =>
        db.Balances.AnyAsync(x => x.LocationId == locationId && x.Quantity > 0, ct);

    public async Task<IReadOnlyList<StockBalance>> GetLocationContentAsync(int locationId, CancellationToken ct = default) =>
        await db.Balances
            .AsNoTracking()
            .Include(x => x.Item).ThenInclude(i => i.Unit)
            .Where(x => x.LocationId == locationId && x.Quantity > 0)
            .ToListAsync(ct);

    public void AddLocation(StorageLocation location) => db.Locations.Add(location);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
