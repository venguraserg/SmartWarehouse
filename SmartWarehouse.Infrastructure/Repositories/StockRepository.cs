using Microsoft.EntityFrameworkCore;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Infrastructure.Data;

namespace SmartWarehouse.Infrastructure.Repositories;

public class StockRepository(WarehouseDbContext db) : IStockRepository
{
    public async Task<IReadOnlyDictionary<int, Item>> GetItemsAsync(IEnumerable<int> ids, CancellationToken ct = default)
    {
        var list = ids.ToList();
        return await db.Items
            .AsNoTracking()
            .Include(x => x.Unit)
            .Where(x => list.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
    }

    public Task<bool> ItemExistsAsync(int id, CancellationToken ct = default) =>
        db.Items.AnyAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyDictionary<int, LocationInfo>> GetLocationsAsync(IEnumerable<int> ids, CancellationToken ct = default)
    {
        var list = ids.ToList();
        return await db.Locations
            .AsNoTracking()
            .Where(x => list.Contains(x.Id))
            .Select(x => new LocationInfo(x.Id, x.FullCode, x.Children.Any()))
            .ToDictionaryAsync(x => x.Id, ct);
    }

    public Task<bool> EquipmentExistsAsync(int equipmentId, CancellationToken ct = default) =>
        db.Equipment.AnyAsync(x => x.Id == equipmentId, ct);

    public Task<StockBalance?> GetBalanceAsync(int itemId, int locationId, CancellationToken ct = default) =>
        db.Balances.FirstOrDefaultAsync(x => x.ItemId == itemId && x.LocationId == locationId, ct);

    public void AddBalance(StockBalance balance) => db.Balances.Add(balance);

    public void AddDocument(StockDocument document) => db.Documents.Add(document);

    public Task<StockDocument?> GetDocumentAsync(long id, CancellationToken ct = default) =>
        db.Documents
            .AsNoTracking()
            .Include(x => x.Lines).ThenInclude(l => l.Item).ThenInclude(i => i.Unit)
            .Include(x => x.Lines).ThenInclude(l => l.FromLocation)
            .Include(x => x.Lines).ThenInclude(l => l.ToLocation)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<DocumentHeader>> ListDocumentsAsync(MovementType? type, int take, CancellationToken ct = default)
    {
        var query = db.Documents.AsNoTracking();
        if (type is not null)
            query = query.Where(x => x.Type == type);

        var rows = await query
            .OrderByDescending(x => x.OccurredAt)
            .ThenByDescending(x => x.Id)
            .Take(take)
            .Select(x => new { Document = x, LineCount = x.Lines.Count })
            .ToListAsync(ct);

        return rows.Select(r => new DocumentHeader(r.Document, r.LineCount)).ToList();
    }

    public async Task<IReadOnlyList<StockBalance>> GetBalancesForItemAsync(int itemId, CancellationToken ct = default) =>
        await db.Balances
            .AsNoTracking()
            .Include(x => x.Location)
            .Where(x => x.ItemId == itemId && x.Quantity > 0)
            .OrderBy(x => x.Location.FullCode)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<StockMovement>> GetMovementsForItemAsync(int itemId, int take, CancellationToken ct = default) =>
        await db.Movements
            .AsNoTracking()
            .Include(x => x.Document)
            .Include(x => x.FromLocation)
            .Include(x => x.ToLocation)
            .Where(x => x.ItemId == itemId)
            .OrderByDescending(x => x.OccurredAt)
            .ThenByDescending(x => x.Id)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ItemStock>> GetItemsBelowMinimumAsync(CancellationToken ct = default)
    {
        var rows = await db.Items
            .AsNoTracking()
            .Include(i => i.Unit)
            .Where(i => i.IsActive)
            .Select(i => new { Item = i, Total = i.Balances.Sum(b => (decimal?)b.Quantity) ?? 0m })
            .Where(x => x.Total < x.Item.MinStock)
            .ToListAsync(ct);

        return rows.Select(x => new ItemStock(x.Item, x.Total)).ToList();
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
