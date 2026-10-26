using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;

namespace SmartWarehouse.Tests;

/// <summary>Репозиторий в памяти для проверки бизнес-правил без базы данных.</summary>
internal class FakeStockRepository : IStockRepository
{
    public List<Item> Items { get; } = [];
    public List<StorageLocation> Locations { get; } = [];
    public List<Equipment> Equipment { get; } = [];
    public List<StockBalance> Balances { get; } = [];
    public List<StockMovement> Movements { get; } = [];
    public int SaveCount { get; private set; }

    private long _nextMovementId = 1;

    public Task<Item?> GetItemAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(x => x.Id == id));

    public Task<bool> LocationExistsAsync(int locationId, CancellationToken ct = default) =>
        Task.FromResult(Locations.Any(x => x.Id == locationId));

    public Task<bool> EquipmentExistsAsync(int equipmentId, CancellationToken ct = default) =>
        Task.FromResult(Equipment.Any(x => x.Id == equipmentId));

    public Task<StockBalance?> GetBalanceAsync(int itemId, int locationId, CancellationToken ct = default) =>
        Task.FromResult(Balances.FirstOrDefault(x => x.ItemId == itemId && x.LocationId == locationId));

    public void AddBalance(StockBalance balance)
    {
        balance.Location = Locations.First(x => x.Id == balance.LocationId);
        Balances.Add(balance);
    }

    public void AddMovement(StockMovement movement)
    {
        movement.Id = _nextMovementId++;
        Movements.Add(movement);
    }

    public Task<IReadOnlyList<StockBalance>> GetBalancesForItemAsync(int itemId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<StockBalance>>(
            Balances.Where(x => x.ItemId == itemId && x.Quantity > 0).OrderBy(x => x.Location.Code).ToList());

    public Task<IReadOnlyList<StockMovement>> GetMovementsForItemAsync(int itemId, int take, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<StockMovement>>(
            Movements.Where(x => x.ItemId == itemId).OrderByDescending(x => x.Id).Take(take).ToList());

    public Task<IReadOnlyList<ItemStock>> GetItemsBelowMinimumAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ItemStock>>(
            Items.Where(i => i.IsActive)
                .Select(i => new ItemStock(i, Balances.Where(b => b.ItemId == i.Id).Sum(b => b.Quantity)))
                .Where(x => x.Total < x.Item.MinStock)
                .ToList());

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

internal class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
