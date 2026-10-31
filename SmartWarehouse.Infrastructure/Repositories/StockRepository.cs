using Microsoft.EntityFrameworkCore;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Application.Services;
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

    public Task<Employee?> GetEmployeeAsync(int id, CancellationToken ct = default) =>
        db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<StockBalance?> GetBalanceAsync(int itemId, int locationId, CancellationToken ct = default) =>
        db.Balances.FirstOrDefaultAsync(x => x.ItemId == itemId && x.LocationId == locationId, ct);

    public void AddBalance(StockBalance balance) => db.Balances.Add(balance);

    public Task<EmployeeBalance?> GetEmployeeBalanceAsync(int itemId, int employeeId, CancellationToken ct = default) =>
        db.EmployeeBalances.FirstOrDefaultAsync(x => x.ItemId == itemId && x.EmployeeId == employeeId, ct);

    public void AddEmployeeBalance(EmployeeBalance balance) => db.EmployeeBalances.Add(balance);

    public void AddDocument(StockDocument document) => db.Documents.Add(document);

    public async Task<decimal> GetReservedAsync(int itemId, int employeeId, int? exceptActId, CancellationToken ct = default) =>
        await db.WriteOffActLines
            .Where(l => l.ItemId == itemId
                && l.Act.EmployeeId == employeeId
                && l.Act.Status == ActStatus.Pending
                && (exceptActId == null || l.ActId != exceptActId))
            .SumAsync(l => (decimal?)l.Quantity, ct) ?? 0;

    public Task<StockDocument?> GetDocumentAsync(long id, CancellationToken ct = default) =>
        db.Documents
            .AsNoTracking()
            .Include(x => x.Lines).ThenInclude(l => l.Item).ThenInclude(i => i.Unit)
            .Include(x => x.Lines).ThenInclude(l => l.FromLocation)
            .Include(x => x.Lines).ThenInclude(l => l.ToLocation)
            .Include(x => x.Act)
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

    public async Task<IReadOnlyList<StockBalance>> SearchBalancesAsync(StockFilter filter, int take, CancellationToken ct = default)
    {
        var query = db.Balances
            .AsNoTracking()
            .Include(b => b.Item).ThenInclude(i => i.Unit)
            .Include(b => b.Item).ThenInclude(i => i.Category)
            .Include(b => b.Location).ThenInclude(l => l.Warehouse)
            .Where(b => b.Quantity > 0);

        if (filter.WarehouseId is int warehouseId)
            query = query.Where(b => b.Location.WarehouseId == warehouseId);

        if (filter.LocationCode is string code)
        {
            // То же правило, что LocationCodeMatch.Matches: совпадение по целым частям кода.
            var subtree = code + LocationCodeMatch.Separator;
            var suffix = LocationCodeMatch.Separator + code;
            var middle = LocationCodeMatch.Separator + code + LocationCodeMatch.Separator;
            query = query.Where(b =>
                b.Location.FullCode == code
                || b.Location.FullCode.StartsWith(subtree)
                || b.Location.FullCode.EndsWith(suffix)
                || b.Location.FullCode.Contains(middle));
        }

        if (filter.Search is string search)
        {
            var pattern = $"%{search}%";
            query = query.Where(b => EF.Functions.ILike(b.Item.Name, pattern) || EF.Functions.ILike(b.Item.Sku, pattern));
        }

        if (filter.CategoryId is int categoryId)
            query = query.Where(b => b.Item.CategoryId == categoryId);

        if (filter.BelowMinimum)
            query = query.Where(b => b.Item.Balances.Sum(x => x.Quantity) < b.Item.MinStock);

        return await query
            .OrderBy(b => b.Location.FullCode)
            .ThenBy(b => b.Item.Name)
            .Take(take)
            .ToListAsync(ct);
    }

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
