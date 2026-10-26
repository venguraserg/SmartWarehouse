using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.Tests;

/// <summary>Репозиторий складских документов в памяти — для проверки бизнес-правил без базы данных.</summary>
internal class FakeStockRepository : IStockRepository
{
    public List<Item> Items { get; } = [];
    public List<StorageLocation> Locations { get; } = [];
    public List<Equipment> Equipment { get; } = [];
    public List<StockBalance> Balances { get; } = [];
    public List<StockDocument> Documents { get; } = [];
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
                .ToDictionary(x => x.Id, x => new LocationInfo(x.Id, x.FullCode, Locations.Any(c => c.ParentId == x.Id))));
    }

    public Task<bool> EquipmentExistsAsync(int equipmentId, CancellationToken ct = default) =>
        Task.FromResult(Equipment.Any(x => x.Id == equipmentId));

    public Task<StockBalance?> GetBalanceAsync(int itemId, int locationId, CancellationToken ct = default) =>
        Task.FromResult(Balances.FirstOrDefault(x => x.ItemId == itemId && x.LocationId == locationId));

    // Как и EF, новые записи попадают в «базу» только при сохранении.
    public void AddBalance(StockBalance balance) => _pendingBalances.Add(balance);

    public void AddDocument(StockDocument document) => _pendingDocuments.Add(document);

    public Task<StockDocument?> GetDocumentAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(Documents.FirstOrDefault(x => x.Id == id));

    public Task<IReadOnlyList<DocumentHeader>> ListDocumentsAsync(MovementType? type, int take, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DocumentHeader>>(
            Documents.Where(x => type is null || x.Type == type)
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

    public Task<IReadOnlyList<ItemStock>> GetItemsBelowMinimumAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ItemStock>>(
            Items.Where(i => i.IsActive)
                .Select(i => new ItemStock(i, Balances.Where(b => b.ItemId == i.Id).Sum(b => b.Quantity)))
                .Where(x => x.Total < x.Item.MinStock)
                .ToList());

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var balance in _pendingBalances)
        {
            balance.Location = Locations.First(x => x.Id == balance.LocationId);
            Balances.Add(balance);
        }

        foreach (var document in _pendingDocuments)
        {
            document.Id = _nextDocumentId++;
            foreach (var line in document.Lines)
            {
                line.Id = _nextMovementId++;
                line.DocumentId = document.Id;
                line.FromLocation = Locations.FirstOrDefault(x => x.Id == line.FromLocationId);
                line.ToLocation = Locations.FirstOrDefault(x => x.Id == line.ToLocationId);
            }
            Documents.Add(document);
        }

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
