using Microsoft.EntityFrameworkCore;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Infrastructure.Data;

namespace SmartWarehouse.Infrastructure.Repositories;

public class StockRepository(WarehouseDbContext db) : IStockRepository
{
    public Task<Item?> GetItemAsync(int id, CancellationToken ct = default) =>
        db.Items.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<bool> LocationExistsAsync(int locationId, CancellationToken ct = default) =>
        db.Locations.AnyAsync(x => x.Id == locationId, ct);

    public Task<bool> EquipmentExistsAsync(int equipmentId, CancellationToken ct = default) =>
        db.Equipment.AnyAsync(x => x.Id == equipmentId, ct);

    public Task<StockBalance?> GetBalanceAsync(int itemId, int locationId, CancellationToken ct = default) =>
        db.Balances.FirstOrDefaultAsync(x => x.ItemId == itemId && x.LocationId == locationId, ct);

    public void AddBalance(StockBalance balance) => db.Balances.Add(balance);

    public void AddMovement(StockMovement movement) => db.Movements.Add(movement);

    public async Task<IReadOnlyList<StockBalance>> GetBalancesForItemAsync(int itemId, CancellationToken ct = default) =>
        await db.Balances
            .AsNoTracking()
            .Include(x => x.Location)
            .Where(x => x.ItemId == itemId && x.Quantity > 0)
            .OrderBy(x => x.Location.Code)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<StockMovement>> GetMovementsForItemAsync(int itemId, int take, CancellationToken ct = default) =>
        await db.Movements
            .AsNoTracking()
            .Where(x => x.ItemId == itemId)
            .OrderByDescending(x => x.OccurredAt)
            .ThenByDescending(x => x.Id)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ItemStock>> GetItemsBelowMinimumAsync(CancellationToken ct = default)
    {
        var rows = await db.Items
            .AsNoTracking()
            .Where(i => i.IsActive)
            .Select(i => new { Item = i, Total = i.Balances.Sum(b => (decimal?)b.Quantity) ?? 0m })
            .Where(x => x.Total < x.Item.MinStock)
            .ToListAsync(ct);

        return rows.Select(x => new ItemStock(x.Item, x.Total)).ToList();
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
