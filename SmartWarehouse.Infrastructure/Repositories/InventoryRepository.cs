using Microsoft.EntityFrameworkCore;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Infrastructure.Data;

namespace SmartWarehouse.Infrastructure.Repositories;

public class InventoryRepository(WarehouseDbContext db) : IInventoryRepository
{
    public Task<InventoryCount?> GetAsync(int id, CancellationToken ct = default) =>
        db.Inventories
            .Include(x => x.RootLocation)
            .Include(x => x.Lines).ThenInclude(l => l.Item).ThenInclude(i => i.Unit)
            .Include(x => x.Places)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<InventoryCount>> ListAsync(int take, CancellationToken ct = default) =>
        await db.Inventories.AsNoTracking()
            .Include(x => x.RootLocation)
            .Include(x => x.Lines)
            .Include(x => x.Places)
            .AsSplitQuery()
            .OrderByDescending(x => x.Id)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<InventoryCount>> ListOpenAsync(CancellationToken ct = default) =>
        await db.Inventories.AsNoTracking()
            .Include(x => x.RootLocation)
            .Where(x => x.Status == InventoryStatus.Open)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<StockBalance>> ListStockAsync(IReadOnlyCollection<int> locationIds, CancellationToken ct = default) =>
        await db.Balances.AsNoTracking()
            .Include(x => x.Item).ThenInclude(i => i.Unit)
            .Include(x => x.Batch)
            .Where(x => locationIds.Contains(x.LocationId) && x.Quantity > 0)
            .ToListAsync(ct);

    public Task<Item?> GetItemAsync(int itemId, CancellationToken ct = default) =>
        db.Items.Include(x => x.Unit).FirstOrDefaultAsync(x => x.Id == itemId, ct);

    public async Task<IReadOnlyList<(int ItemId, int LocationId, decimal Quantity)>> ListItemPlacesAsync(
        IReadOnlyCollection<int> itemIds, CancellationToken ct = default)
    {
        var rows = await db.Balances.AsNoTracking()
            .Where(x => itemIds.Contains(x.ItemId) && x.Quantity > 0)
            .GroupBy(x => new { x.ItemId, x.LocationId })
            .Select(g => new { g.Key.ItemId, g.Key.LocationId, Quantity = g.Sum(x => x.Quantity) })
            .ToListAsync(ct);
        return rows.Select(r => (r.ItemId, r.LocationId, r.Quantity)).ToList();
    }

    public async Task SetCheckedAsync(IReadOnlyCollection<int> locationIds, DateTimeOffset at, CancellationToken ct = default)
    {
        foreach (var location in await db.Locations.Where(x => locationIds.Contains(x.Id)).ToListAsync(ct))
            location.CheckedAt = at;
    }

    public void Add(InventoryCount inventory) => db.Inventories.Add(inventory);

    public void Remove(InventoryLine line) => db.InventoryLines.Remove(line);

    public void Remove(InventoryPlace place) => db.InventoryPlaces.Remove(place);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public async Task InTransactionAsync(Func<Task> action, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await action();
        await transaction.CommitAsync(ct);
    }
}
