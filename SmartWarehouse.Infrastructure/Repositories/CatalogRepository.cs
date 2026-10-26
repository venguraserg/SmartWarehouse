using Microsoft.EntityFrameworkCore;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Infrastructure.Data;

namespace SmartWarehouse.Infrastructure.Repositories;

public class CatalogRepository(WarehouseDbContext db) : ICatalogRepository
{
    private const int MaxListSize = 500;

    public Task<Item?> GetItemAsync(int id, CancellationToken ct = default) =>
        db.Items.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<bool> SkuExistsAsync(string sku, CancellationToken ct = default) =>
        db.Items.AnyAsync(x => x.Sku == sku, ct);

    public async Task<IReadOnlyList<Item>> ListItemsAsync(string? search, CancellationToken ct = default)
    {
        var query = db.Items.AsNoTracking();

        if (!string.IsNullOrEmpty(search))
        {
            var pattern = $"%{search}%";
            query = query.Where(x => EF.Functions.ILike(x.Name, pattern) || EF.Functions.ILike(x.Sku, pattern));
        }

        return await query.OrderBy(x => x.Name).Take(MaxListSize).ToListAsync(ct);
    }

    public void AddItem(Item item) => db.Items.Add(item);

    public Task<Warehouse?> GetWarehouseAsync(int id, CancellationToken ct = default) =>
        db.Warehouses.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Warehouse>> ListWarehousesAsync(CancellationToken ct = default) =>
        await db.Warehouses
            .AsNoTracking()
            .Include(x => x.Locations.OrderBy(l => l.Code))
            .OrderBy(x => x.Name)
            .ToListAsync(ct);

    public void AddWarehouse(Warehouse warehouse) => db.Warehouses.Add(warehouse);

    public Task<bool> LocationCodeExistsAsync(string code, CancellationToken ct = default) =>
        db.Locations.AnyAsync(x => x.Code == code, ct);

    public void AddLocation(StorageLocation location) => db.Locations.Add(location);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
