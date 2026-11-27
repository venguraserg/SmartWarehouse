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
            .Select(x => new LocationInfo(x.Id, x.FullCode, x.Children.Any(), x.Name))
            .ToDictionaryAsync(x => x.Id, ct);
    }

    public Task<bool> EquipmentExistsAsync(int equipmentId, CancellationToken ct = default) =>
        db.Equipment.AnyAsync(x => x.Id == equipmentId, ct);

    public Task<Employee?> GetEmployeeAsync(int id, CancellationToken ct = default) =>
        db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<StockBalance>> GetLocationBatchesAsync(int itemId, int locationId, CancellationToken ct = default) =>
        await db.Balances.Include(x => x.Batch).Where(x => x.ItemId == itemId && x.LocationId == locationId).ToListAsync(ct);

    public void AddBalance(StockBalance balance) => db.Balances.Add(balance);

    public async Task<IReadOnlyList<EmployeeBalance>> GetEmployeeBatchesAsync(int itemId, int employeeId, CancellationToken ct = default) =>
        await db.EmployeeBalances.Include(x => x.Batch).Where(x => x.ItemId == itemId && x.EmployeeId == employeeId).ToListAsync(ct);

    public void AddEmployeeBalance(EmployeeBalance balance) => db.EmployeeBalances.Add(balance);

    public Task<Batch?> GetBatchAsync(int id, CancellationToken ct = default) =>
        db.Batches.FirstOrDefaultAsync(x => x.Id == id, ct);

    public void AddBatch(Batch batch) => db.Batches.Add(batch);

    public async Task<IReadOnlyList<int>> ListStorekeeperIdsAsync(CancellationToken ct = default) =>
        (await db.Users.AsNoTracking().Where(u => u.IsActive && u.EmployeeId != null).ToListAsync(ct))
            .Where(u => u.Has(UserRole.Storekeeper))
            .Select(u => u.EmployeeId!.Value)
            .Distinct()
            .ToList();

    public async Task<IReadOnlyDictionary<(int BatchId, CustodyKind Kind), decimal>> GetReservedByBatchAsync(int itemId, int employeeId, int? exceptActId, CancellationToken ct = default)
    {
        var rows = await ReservedLines(db.WriteOffActLines)
            .Where(l => l.ItemId == itemId
                && l.BatchId != null
                && l.Act.EmployeeId == employeeId
                && (exceptActId == null || (l.ActId != exceptActId && l.Act.SummaryActId != exceptActId)))
            .GroupBy(l => new { BatchId = l.BatchId!.Value, l.Act.Kind })
            .Select(g => new { g.Key.BatchId, g.Key.Kind, Quantity = g.Sum(l => l.Quantity) })
            .ToListAsync(ct);
        return rows.ToDictionary(
            r => (r.BatchId, r.Kind == ActKind.WriteOff ? CustodyKind.Official : CustodyKind.Work),
            r => r.Quantity);
    }

    /// <summary>
    /// Строки, которые держат подотчёт в резерве: акт или отчёт на утверждении (подтверждении),
    /// а также подтверждённый отчёт об использовании, пока строка не списана сводным актом кладовщика
    /// (вне учёта списывается сразу при подтверждении).
    /// </summary>
    internal static IQueryable<WriteOffActLine> ReservedLines(IQueryable<WriteOffActLine> lines) =>
        lines.Where(l => l.Act.Status == ActStatus.Pending
            || (l.Act.Kind == ActKind.UsageReport && l.Act.Status == ActStatus.Approved && l.DocumentId == null
                && l.Batch != null && !l.Batch.OffBook));

    // ---------- Приёмка по накладным ----------

    public Task<bool> ReceiptExistsAsync(string externalNumber, CancellationToken ct = default) =>
        db.Documents.AnyAsync(d => (d.Type == MovementType.Receipt || d.Type == MovementType.MolChange) && d.ExternalNumber == externalNumber, ct);

    public async Task<IReadOnlyList<StockBalance>> ListLocationStockAsync(int locationId, CancellationToken ct = default) =>
        await db.Balances
            .AsNoTracking()
            .Include(b => b.Item).ThenInclude(i => i.Unit)
            .Include(b => b.Batch).ThenInclude(x => x.Mol)
            .Where(b => b.LocationId == locationId && b.Quantity != 0)
            .OrderBy(b => b.Batch.ReceivedAt).ThenBy(b => b.Item.Name)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ManualBatch>> ListManualAccountedAsync(IReadOnlyCollection<int> itemIds, CancellationToken ct = default)
    {
        var ids = itemIds.ToList();
        var rows = await db.Balances
            .Where(b => ids.Contains(b.ItemId) && b.Quantity > 0 && !b.Batch.OffBook && b.Batch.Origin == BatchOrigin.Manual
                        && b.Batch.ReceiptDocumentId != null)
            .GroupBy(b => new { b.ItemId, b.BatchId, b.Batch.ReceiptDocumentId, b.Batch.ReceivedAt })
            .Select(g => new { g.Key.ItemId, g.Key.BatchId, g.Key.ReceiptDocumentId, g.Key.ReceivedAt, Quantity = g.Sum(b => b.Quantity) })
            .ToListAsync(ct);
        return rows
            .Select(r => new ManualBatch(r.ItemId, r.BatchId, r.ReceiptDocumentId is long id ? StockDocument.FormatNumber(MovementType.Receipt, id) : null,
                r.ReceivedAt, r.Quantity))
            .ToList();
    }

    public async Task<decimal> GetBatchReceivedAsync(int batchId, CancellationToken ct = default) =>
        await db.Movements
            .Where(m => m.BatchId == batchId && m.Document.Type == MovementType.Receipt)
            .SumAsync(m => (decimal?)m.Quantity, ct) ?? 0;

    public void AddDiscrepancy(ReceiptDiscrepancy discrepancy) => db.ReceiptDiscrepancies.Add(discrepancy);

    public Task<ReceiptDiscrepancy?> GetDiscrepancyAsync(int id, CancellationToken ct = default) =>
        db.ReceiptDiscrepancies.Include(d => d.Batch).ThenInclude(b => b.Item).ThenInclude(i => i.Unit)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<IReadOnlyList<ReceiptDiscrepancy>> ListDiscrepanciesAsync(bool includeResolved, CancellationToken ct = default) =>
        await db.ReceiptDiscrepancies
            .AsNoTracking()
            .Include(d => d.Batch).ThenInclude(b => b.Item).ThenInclude(i => i.Unit)
            .Where(d => includeResolved || d.ResolvedAt == null)
            .OrderByDescending(d => d.CreatedAt)
            .Take(500)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<MolTotal>> ListMolTotalsAsync(CancellationToken ct = default)
    {
        // Склад (все места, включая «Место не указано») и «выдано в работу» — по пометке МОЛ партии.
        var stock = await db.Balances
            .Where(b => b.Quantity != 0 && !b.Batch.OffBook)
            .GroupBy(b => new { b.Batch.MolId, b.ItemId })
            .Select(g => new
            {
                g.Key.MolId,
                g.Key.ItemId,
                Quantity = g.Sum(b => b.Quantity),
                Value = g.Sum(b => b.Quantity * (b.Batch.UnitPrice ?? 0)),
                Incomplete = g.Any(b => b.Batch.UnitPrice == null)
            })
            .ToListAsync(ct);
        var atWork = await db.EmployeeBalances
            .Where(b => b.Quantity != 0 && b.Kind == CustodyKind.Work && !b.Batch.OffBook)
            .GroupBy(b => new { b.Batch.MolId, b.ItemId })
            .Select(g => new
            {
                g.Key.MolId,
                g.Key.ItemId,
                Quantity = g.Sum(b => b.Quantity),
                Value = g.Sum(b => b.Quantity * (b.Batch.UnitPrice ?? 0)),
                Incomplete = g.Any(b => b.Batch.UnitPrice == null)
            })
            .ToListAsync(ct);
        var totals = stock.Concat(atWork)
            .GroupBy(t => (t.MolId, t.ItemId))
            .Select(g => new { g.Key.MolId, g.Key.ItemId, Quantity = g.Sum(t => t.Quantity), Value = g.Sum(t => t.Value), Incomplete = g.Any(t => t.Incomplete) })
            .ToList();
        var items = await ItemNamesAsync(totals.Select(t => t.ItemId), ct);
        return totals
            .Select(t => new MolTotal(t.MolId, t.ItemId, items[t.ItemId].Sku, items[t.ItemId].Name, items[t.ItemId].Unit,
                t.Quantity, decimal.Round(t.Value, 2), t.Incomplete))
            .ToList();
    }

    public async Task<IReadOnlyList<StockBalance>> ListMolSourcesAsync(IReadOnlyCollection<int> itemIds, CancellationToken ct = default)
    {
        var ids = itemIds.Distinct().ToList();
        return await db.Balances.AsNoTracking()
            .Include(b => b.Location)
            .Include(b => b.Batch)
            .Where(b => ids.Contains(b.ItemId) && b.Quantity > 0 && !b.Batch.OffBook)
            .OrderBy(b => b.Batch.ReceivedAt).ThenBy(b => b.BatchId)
            .ToListAsync(ct);
    }

    public async Task<decimal> GetBatchTotalAsync(int batchId, CancellationToken ct = default) =>
        await db.Balances.Where(b => b.BatchId == batchId).SumAsync(b => b.Quantity, ct)
        + await db.EmployeeBalances.Where(b => b.BatchId == batchId).SumAsync(b => b.Quantity, ct);


    private async Task<Dictionary<int, (string Sku, string Name, string Unit)>> ItemNamesAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Items
            .Where(i => list.Contains(i.Id))
            .Select(i => new { i.Id, i.Sku, i.Name, Unit = i.Unit.Code })
            .ToDictionaryAsync(i => i.Id, i => (i.Sku, i.Name, i.Unit), ct);
    }

    public async Task<IReadOnlyList<BatchStock>> ListBatchStockAsync(int itemId, int? locationId, int? employeeId, CancellationToken ct = default)
    {
        List<(int BatchId, decimal Quantity)> amounts;
        if (locationId is int location)
        {
            amounts = (await db.Balances.AsNoTracking()
                    .Where(x => x.ItemId == itemId && x.LocationId == location && x.Quantity > 0)
                    .Select(x => new { x.BatchId, x.Quantity }).ToListAsync(ct))
                .Select(x => (x.BatchId, x.Quantity)).ToList();
        }
        else if (employeeId is int employee)
        {
            amounts = (await db.EmployeeBalances.AsNoTracking()
                    .Where(x => x.ItemId == itemId && x.EmployeeId == employee && x.Quantity > 0)
                    .Select(x => new { x.BatchId, x.Quantity }).ToListAsync(ct))
                .Select(x => (x.BatchId, x.Quantity)).ToList();
        }
        else
        {
            var stock = await db.Balances.AsNoTracking().Where(x => x.ItemId == itemId && x.Quantity > 0)
                .Select(x => new { x.BatchId, x.Quantity }).ToListAsync(ct);
            var custody = await db.EmployeeBalances.AsNoTracking().Where(x => x.ItemId == itemId && x.Quantity > 0)
                .Select(x => new { x.BatchId, x.Quantity }).ToListAsync(ct);
            amounts = stock.Concat(custody).Select(x => (x.BatchId, x.Quantity)).ToList();
        }

        var totals = amounts.GroupBy(a => a.BatchId).ToDictionary(g => g.Key, g => g.Sum(a => a.Quantity));
        var ids = totals.Keys.ToList();
        var batches = await db.Batches.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new { Batch = x, Document = x.ReceiptDocument })
            .ToListAsync(ct);
        return batches
            .Select(b => new BatchStock(b.Batch, totals[b.Batch.Id], b.Document == null ? null : b.Document.Number))
            .ToList();
    }

    public void AddDocument(StockDocument document) => db.Documents.Add(document);

    public async Task<decimal> GetReservedAsync(int itemId, int employeeId, int? exceptActId, CancellationToken ct = default) =>
        await StockRepository.ReservedLines(db.WriteOffActLines)
            .Where(l => l.ItemId == itemId
                && l.Act.EmployeeId == employeeId
                && (exceptActId == null || (l.ActId != exceptActId && l.Act.SummaryActId != exceptActId)))
            .SumAsync(l => (decimal?)l.Quantity, ct) ?? 0;

    public Task<StockDocument?> GetDocumentAsync(long id, CancellationToken ct = default) =>
        db.Documents
            .AsNoTracking()
            .Include(x => x.Lines).ThenInclude(l => l.Item).ThenInclude(i => i.Unit)
            .Include(x => x.Lines).ThenInclude(l => l.FromLocation)
            .Include(x => x.Lines).ThenInclude(l => l.ToLocation)
            .Include(x => x.Lines).ThenInclude(l => l.Batch).ThenInclude(b => b.Mol)
            .Include(x => x.Act)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<DocumentHeader>> ListDocumentsAsync(
        MovementType? type, int take, IReadOnlyCollection<int>? onlyEmployeeIds = null, CancellationToken ct = default)
    {
        var query = db.Documents.AsNoTracking();
        if (type is not null)
            query = query.Where(x => x.Type == type);
        if (onlyEmployeeIds is not null)
        {
            var ids = onlyEmployeeIds.ToList();
            query = query.Where(x => (x.ReceivedById != null && ids.Contains(x.ReceivedById.Value))
                || (x.PerformedById != null && ids.Contains(x.PerformedById.Value)));
        }

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
            .Include(x => x.Batch)
            .Where(x => x.ItemId == itemId && x.Quantity > 0)
            .OrderBy(x => x.Location.FullCode)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<StockMovement>> GetMovementsForItemAsync(int itemId, int take, CancellationToken ct = default) =>
        await db.Movements
            .AsNoTracking()
            .Include(x => x.Document)
            .Include(x => x.FromLocation)
            .Include(x => x.ToLocation)
            .Include(x => x.Batch)
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
            .Include(b => b.Batch).ThenInclude(x => x.Mol)
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

        // По словам: каждое — в названии, артикуле или заметке сотрудника к позиции
        // («гомогенизатор» найдёт подшипник, у которого так написано в заметке).
        foreach (var word in SearchWords.Split(filter.Search))
        {
            var pattern = SearchWords.LikePattern(word);
            query = query.Where(b => EF.Functions.ILike(b.Item.Name, pattern, SearchWords.LikeEscape)
                || EF.Functions.ILike(b.Item.Sku, pattern, SearchWords.LikeEscape)
                || db.ItemNotes.Any(n => n.ItemId == b.ItemId && EF.Functions.ILike(n.Text, pattern, SearchWords.LikeEscape)));
        }

        if (filter.CategoryId is int categoryId)
            query = query.Where(b => b.Item.CategoryId == categoryId);

        if (filter.OffBook is bool offBook)
            query = query.Where(b => b.Batch.OffBook == offBook);

        if (filter.MolId is 0)
            query = query.Where(b => b.Batch.MolId == null);
        else if (filter.MolId is int molId)
            query = query.Where(b => b.Batch.MolId == molId);

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

    public async Task<(int Items, int Locations, int CustodyEmployees, int CustodyItems)> CountStockAsync(CancellationToken ct = default)
    {
        var stock = db.Balances.AsNoTracking().Where(b => b.Quantity > 0);
        var custody = db.EmployeeBalances.AsNoTracking().Where(b => b.Quantity > 0);
        return (
            await stock.Select(b => b.ItemId).Distinct().CountAsync(ct),
            await stock.Select(b => b.LocationId).Distinct().CountAsync(ct),
            await custody.Select(b => b.EmployeeId).Distinct().CountAsync(ct),
            await custody.Select(b => new { b.EmployeeId, b.ItemId }).Distinct().CountAsync(ct));
    }

    public async Task<(int Total, IReadOnlyList<StalePlace> Places)> GetStalePlacesAsync(DateTimeOffset before, int take, CancellationToken ct = default)
    {
        var places = db.Balances.AsNoTracking()
            .Where(b => b.Quantity > 0)
            .GroupBy(b => new { b.LocationId, b.Location.FullCode, b.Location.CheckedAt })
            .Where(g => g.Key.CheckedAt == null || g.Key.CheckedAt < before)
            .Select(g => new { g.Key.LocationId, g.Key.FullCode, g.Key.CheckedAt, Items = g.Select(x => x.ItemId).Distinct().Count() });

        var total = await places.CountAsync(ct);
        var rows = await places
            .OrderBy(p => p.CheckedAt != null)      // ни разу не проверенные — первыми
            .ThenBy(p => p.CheckedAt)
            .ThenBy(p => p.FullCode)
            .Take(take)
            .ToListAsync(ct);
        return (total, rows.Select(p => new StalePlace(p.LocationId, p.FullCode, p.CheckedAt, p.Items)).ToList());
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
