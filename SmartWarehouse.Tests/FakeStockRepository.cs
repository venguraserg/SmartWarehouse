using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.Tests;

/// <summary>Репозиторий складских документов в памяти — для проверки бизнес-правил без базы данных.</summary>
internal class FakeStockRepository : IStockRepository
{
    public List<Item> Items { get; } = [];
    public List<StorageLocation> Locations { get; } = [];
    public List<Equipment> Equipment { get; } = [];
    public List<Employee> Employees { get; } = [];
    public List<EmployeeBalance> EmployeeBalances { get; } = [];
    private readonly List<EmployeeBalance> _pendingEmployeeBalances = [];
    public List<StockBalance> Balances { get; } = [];
    public List<StockDocument> Documents { get; } = [];
    public List<WriteOffAct> Acts { get; } = [];
    public int SaveCount { get; private set; }

    private readonly List<StockBalance> _pendingBalances = [];
    private readonly List<StockDocument> _pendingDocuments = [];
    private long _nextDocumentId = 1;
    private long _nextMovementId = 1;

    public Task<IReadOnlyDictionary<int, Item>> GetItemsAsync(IEnumerable<int> ids, CancellationToken ct = default)
    {
        var set = ids.ToHashSet();
        return Task.FromResult<IReadOnlyDictionary<int, Item>>(Items.Where(x => set.Contains(x.Id)).ToDictionary(x => x.Id));
    }

    public Task<bool> ItemExistsAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Items.Any(x => x.Id == id));

    public Task<IReadOnlyDictionary<int, LocationInfo>> GetLocationsAsync(IEnumerable<int> ids, CancellationToken ct = default)
    {
        var set = ids.ToHashSet();
        return Task.FromResult<IReadOnlyDictionary<int, LocationInfo>>(
            Locations.Where(x => set.Contains(x.Id))
                .ToDictionary(x => x.Id, x => new LocationInfo(x.Id, x.FullCode, Locations.Any(c => c.ParentId == x.Id), x.Name)));
    }

    public Task<bool> EquipmentExistsAsync(int equipmentId, CancellationToken ct = default) =>
        Task.FromResult(Equipment.Any(x => x.Id == equipmentId));

    public Task<Employee?> GetEmployeeAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Employees.FirstOrDefault(x => x.Id == id));

    public List<Batch> Batches { get; } = [];
    private readonly List<Batch> _pendingBatches = [];
    private int _nextBatchId = 1;

    public Task<IReadOnlyList<StockBalance>> GetLocationBatchesAsync(int itemId, int locationId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<StockBalance>>(Balances.Where(x => x.ItemId == itemId && x.LocationId == locationId).ToList());

    public Task<IReadOnlyList<EmployeeBalance>> GetEmployeeBatchesAsync(int itemId, int employeeId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EmployeeBalance>>(EmployeeBalances.Where(x => x.ItemId == itemId && x.EmployeeId == employeeId).ToList());

    public Task<Batch?> GetBatchAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Batches.FirstOrDefault(x => x.Id == id));

    public void AddBatch(Batch batch) => _pendingBatches.Add(batch);

    public Task<IReadOnlyDictionary<(int BatchId, CustodyKind Kind), decimal>> GetReservedByBatchAsync(int itemId, int employeeId, int? exceptActId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<(int BatchId, CustodyKind Kind), decimal>>(Acts
            .Where(a => a.EmployeeId == employeeId && a.Id != exceptActId && (exceptActId is null || a.SummaryActId != exceptActId))
            .SelectMany(a => a.Lines.Select(l => (Act: a, Line: l)))
            .Where(x => x.Line.ItemId == itemId && x.Line.BatchId is not null
                && (x.Act.Status == ActStatus.Pending
                    || (x.Act.Kind == ActKind.UsageReport && x.Act.Status == ActStatus.Approved && x.Line.DocumentId is null
                        && x.Line.Document is null && BatchOf(x.Line.BatchId!.Value)?.OffBook != true)))
            .GroupBy(x => (x.Line.BatchId!.Value, x.Act.Custody))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Line.Quantity)));

    public Task<IReadOnlyList<MolTotal>> ListMolTotalsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<MolTotal>>(Balances
            .Select(b => (b.ItemId, b.BatchId, b.Quantity))
            .Concat(EmployeeBalances.Where(b => b.Kind == CustodyKind.Work).Select(b => (b.ItemId, b.BatchId, b.Quantity)))
            .Where(b => b.Quantity != 0 && BatchOf(b.BatchId)?.OffBook != true)
            .GroupBy(b => (BatchOf(b.BatchId)?.MolId, b.ItemId))
            .Select(g =>
            {
                var item = Items.First(i => i.Id == g.Key.ItemId);
                return new MolTotal(g.Key.MolId, g.Key.ItemId, item.Sku, item.Name, item.Unit?.Code ?? "",
                    g.Sum(b => b.Quantity), g.Sum(b => b.Quantity * (BatchOf(b.BatchId)?.UnitPrice ?? 0)),
                    g.Any(b => BatchOf(b.BatchId)?.UnitPrice is null));
            })
            .ToList());

    public Task<IReadOnlyList<StockBalance>> ListMolSourcesAsync(IReadOnlyCollection<int> itemIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<StockBalance>>(Balances
            .Where(b => itemIds.Contains(b.ItemId) && b.Quantity > 0 && BatchOf(b.BatchId)?.OffBook != true)
            .Select(b =>
            {
                b.Batch ??= BatchOf(b.BatchId)!;
                b.Location ??= Locations.First(l => l.Id == b.LocationId);
                return b;
            })
            .OrderBy(b => b.Batch.ReceivedAt).ThenBy(b => b.BatchId)
            .ToList());

    public Task<decimal> GetBatchTotalAsync(int batchId, CancellationToken ct = default) =>
        Task.FromResult(Balances.Where(b => b.BatchId == batchId).Sum(b => b.Quantity)
                        + EmployeeBalances.Where(b => b.BatchId == batchId).Sum(b => b.Quantity));

    /// <summary>Кладовщики-МОЛ (сотрудники с учётной записью и ролью «Кладовщик»).</summary>
    public List<int> Storekeepers { get; } = [];

    public Task<IReadOnlyList<int>> ListStorekeeperIdsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<int>>(Storekeepers.ToList());

    private Batch? BatchOf(int id) => Batches.FirstOrDefault(b => b.Id == id);

    // ---------- Приёмка по накладным ----------

    public List<ReceiptDiscrepancy> Discrepancies { get; } = [];

    public Task<bool> ReceiptExistsAsync(string externalNumber, CancellationToken ct = default) =>
        Task.FromResult(Documents.Any(d => d.Type is MovementType.Receipt or MovementType.MolChange && d.ExternalNumber == externalNumber));

    public Task<IReadOnlyList<StockBalance>> ListLocationStockAsync(int locationId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<StockBalance>>(Balances
            .Where(b => b.LocationId == locationId && b.Quantity != 0)
            .Select(b =>
            {
                b.Item ??= Items.First(i => i.Id == b.ItemId);
                b.Batch ??= BatchOf(b.BatchId)!;
                return b;
            })
            .ToList());

    public Task<IReadOnlyList<ManualBatch>> ListManualAccountedAsync(IReadOnlyCollection<int> itemIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ManualBatch>>(Balances
            .Where(b => itemIds.Contains(b.ItemId) && b.Quantity > 0 && BatchOf(b.BatchId) is { OffBook: false, Origin: BatchOrigin.Manual, ReceiptDocumentId: not null })
            .GroupBy(b => b.BatchId)
            .Select(g =>
            {
                var batch = BatchOf(g.Key)!;
                return new ManualBatch(batch.ItemId, batch.Id, StockDocument.FormatNumber(MovementType.Receipt, batch.ReceiptDocumentId!.Value),
                    batch.ReceivedAt, g.Sum(b => b.Quantity));
            })
            .ToList());

    public Task<decimal> GetBatchReceivedAsync(int batchId, CancellationToken ct = default) =>
        Task.FromResult(Documents.Where(d => d.Type == MovementType.Receipt)
            .SelectMany(d => d.Lines).Where(m => m.BatchId == batchId).Sum(m => m.Quantity));

    public void AddDiscrepancy(ReceiptDiscrepancy discrepancy)
    {
        discrepancy.Id = Discrepancies.Count + 1;
        discrepancy.Batch ??= BatchOf(discrepancy.BatchId)!;
        discrepancy.Batch.Item ??= Items.First(i => i.Id == discrepancy.Batch.ItemId);
        Discrepancies.Add(discrepancy);
    }

    public Task<ReceiptDiscrepancy?> GetDiscrepancyAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Discrepancies.FirstOrDefault(d => d.Id == id));

    public Task<IReadOnlyList<ReceiptDiscrepancy>> ListDiscrepanciesAsync(bool includeResolved, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ReceiptDiscrepancy>>(Discrepancies.Where(d => includeResolved || d.ResolvedAt is null).ToList());

    public Task<IReadOnlyList<BatchStock>> ListBatchStockAsync(int itemId, int? locationId, int? employeeId, CancellationToken ct = default)
    {
        IEnumerable<(int BatchId, decimal Quantity)> amounts = locationId is int l
            ? Balances.Where(b => b.ItemId == itemId && b.LocationId == l).Select(b => (b.BatchId, b.Quantity))
            : employeeId is int e
                ? EmployeeBalances.Where(b => b.ItemId == itemId && b.EmployeeId == e).Select(b => (b.BatchId, b.Quantity))
                : Balances.Where(b => b.ItemId == itemId).Select(b => (b.BatchId, b.Quantity))
                    .Concat(EmployeeBalances.Where(b => b.ItemId == itemId).Select(b => (b.BatchId, b.Quantity)));
        return Task.FromResult<IReadOnlyList<BatchStock>>(amounts
            .Where(a => a.Quantity > 0)
            .GroupBy(a => a.BatchId)
            .Select(g => new BatchStock(Batches.First(b => b.Id == g.Key), g.Sum(a => a.Quantity), null))
            .ToList());
    }

    // Как и EF, новые записи попадают в «базу» только при сохранении.
    public void AddBalance(StockBalance balance) => _pendingBalances.Add(balance);

    public void AddDocument(StockDocument document) => _pendingDocuments.Add(document);


    public void AddEmployeeBalance(EmployeeBalance balance) => _pendingEmployeeBalances.Add(balance);

    public Task<decimal> GetReservedAsync(int itemId, int employeeId, int? exceptActId, CancellationToken ct = default) =>
        Task.FromResult(Acts
            .Where(a => a.EmployeeId == employeeId && a.Status == ActStatus.Pending && a.Id != exceptActId)
            .SelectMany(a => a.Lines)
            .Where(l => l.ItemId == itemId)
            .Sum(l => l.Quantity));

    public Task<StockDocument?> GetDocumentAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(Documents.FirstOrDefault(x => x.Id == id));

    public Task<IReadOnlyList<DocumentHeader>> ListDocumentsAsync(
        MovementType? type, int take, IReadOnlyCollection<int>? onlyEmployeeIds = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DocumentHeader>>(
            Documents.Where(x => type is null || x.Type == type)
                .Where(x => onlyEmployeeIds is null
                    || (x.ReceivedById is int r && onlyEmployeeIds.Contains(r))
                    || (x.PerformedById is int p && onlyEmployeeIds.Contains(p)))
                .OrderByDescending(x => x.Id)
                .Take(take)
                .Select(x => new DocumentHeader(x, x.Lines.Count))
                .ToList());

    public Task<IReadOnlyList<StockBalance>> GetBalancesForItemAsync(int itemId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<StockBalance>>(
            Balances.Where(x => x.ItemId == itemId && x.Quantity > 0).OrderBy(x => x.Location.FullCode).ToList());

    public Task<IReadOnlyList<StockMovement>> GetMovementsForItemAsync(int itemId, int take, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<StockMovement>>(
            Documents.SelectMany(d => d.Lines).Where(x => x.ItemId == itemId).OrderByDescending(x => x.Id).Take(take).ToList());

    public Task<IReadOnlyList<StockBalance>> SearchBalancesAsync(StockFilter filter, int take, CancellationToken ct = default)
    {
        IEnumerable<StockBalance> query = Balances.Where(b => b.Quantity > 0);

        foreach (var b in Balances)
            b.Item = Items.First(i => i.Id == b.ItemId);

        if (filter.WarehouseId is int w)
            query = query.Where(b => b.Location.WarehouseId == w);
        if (filter.LocationCode is string code)
            query = query.Where(b => LocationCodeMatch.Matches(b.Location.FullCode, code));
        if (filter.Search is string s)
            query = query.Where(b => b.Item.Name.Contains(s, StringComparison.OrdinalIgnoreCase) || b.Item.Sku.Contains(s, StringComparison.OrdinalIgnoreCase));
        if (filter.CategoryId is int c)
            query = query.Where(b => b.Item.CategoryId == c);
        if (filter.OffBook is bool offBook)
            query = query.Where(b => (BatchOf(b.BatchId)?.OffBook ?? false) == offBook);
        if (filter.MolId is 0)
            query = query.Where(b => BatchOf(b.BatchId)?.MolId is null);
        else if (filter.MolId is int molId)
            query = query.Where(b => BatchOf(b.BatchId)?.MolId == molId);
        if (filter.BelowMinimum)
            query = query.Where(b => Balances.Where(x => x.ItemId == b.ItemId).Sum(x => x.Quantity) < b.Item.MinStock);

        return Task.FromResult<IReadOnlyList<StockBalance>>(
            query.OrderBy(b => b.Location.FullCode, StringComparer.Ordinal).ThenBy(b => b.Item.Name).Take(take).ToList());
    }

    public Task<(int Items, int Locations, int CustodyEmployees, int CustodyItems)> CountStockAsync(CancellationToken ct = default) =>
        Task.FromResult((
            Balances.Where(b => b.Quantity > 0).Select(b => b.ItemId).Distinct().Count(),
            Balances.Where(b => b.Quantity > 0).Select(b => b.LocationId).Distinct().Count(),
            EmployeeBalances.Where(b => b.Quantity > 0).Select(b => b.EmployeeId).Distinct().Count(),
            EmployeeBalances.Where(b => b.Quantity > 0).Select(b => (b.EmployeeId, b.ItemId)).Distinct().Count()));

    public Task<IReadOnlyList<ItemStock>> GetItemsBelowMinimumAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ItemStock>>(
            Items.Where(i => i.IsActive)
                .Select(i => new ItemStock(i, Balances.Where(b => b.ItemId == i.Id).Sum(b => b.Quantity)))
                .Where(x => x.Total < x.Item.MinStock)
                .ToList());

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        // Как EF: сначала новые партии получают номера, затем ссылки на них.
        foreach (var batch in _pendingBatches)
        {
            batch.Id = _nextBatchId++;
            Batches.Add(batch);
        }
        _pendingBatches.Clear();
        foreach (var batch in Batches)
            batch.Mol = batch.MolId is int molId ? Employees.FirstOrDefault(e => e.Id == molId) : null;
        foreach (var b in _pendingBalances)
            if (b.Batch is not null) b.BatchId = b.Batch.Id;
        foreach (var b in _pendingEmployeeBalances)
            if (b.Batch is not null) b.BatchId = b.Batch.Id;

        foreach (var balance in _pendingBalances)
        {
            balance.Location = Locations.First(x => x.Id == balance.LocationId);
            Balances.Add(balance);
        }

        foreach (var document in _pendingDocuments)
        {
            document.Id = _nextDocumentId++;
            foreach (var batch in Batches.Where(b => b.ReceiptDocument == document))
                batch.ReceiptDocumentId = document.Id;
            foreach (var line in document.Lines)
            {
                if (line.Batch is not null) line.BatchId = line.Batch.Id;
                line.Id = _nextMovementId++;
                line.DocumentId = document.Id;
                line.FromLocation = Locations.FirstOrDefault(x => x.Id == line.FromLocationId);
                line.ToLocation = Locations.FirstOrDefault(x => x.Id == line.ToLocationId);
            }
            Documents.Add(document);
        }

        EmployeeBalances.AddRange(_pendingEmployeeBalances);
        _pendingEmployeeBalances.Clear();
        _pendingBalances.Clear();
        _pendingDocuments.Clear();
        SaveCount++;
        return Task.CompletedTask;
    }
}

internal class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
