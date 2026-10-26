using SmartWarehouse.Domain.Entities;

namespace SmartWarehouse.Application.Interfaces;

/// <summary>Позиция номенклатуры с суммарным остатком по всем местам.</summary>
public record ItemStock(Item Item, decimal Total);

/// <summary>Доступ к данным для складских операций.</summary>
public interface IStockRepository
{
    Task<Item?> GetItemAsync(int id, CancellationToken ct = default);

    Task<bool> LocationExistsAsync(int locationId, CancellationToken ct = default);

    Task<bool> EquipmentExistsAsync(int equipmentId, CancellationToken ct = default);

    /// <summary>Остаток позиции на месте (отслеживаемая сущность) или null, если записи ещё нет.</summary>
    Task<StockBalance?> GetBalanceAsync(int itemId, int locationId, CancellationToken ct = default);

    void AddBalance(StockBalance balance);

    void AddMovement(StockMovement movement);

    /// <summary>Остатки позиции по местам (с кодом места), без нулевых.</summary>
    Task<IReadOnlyList<StockBalance>> GetBalancesForItemAsync(int itemId, CancellationToken ct = default);

    /// <summary>Последние движения позиции, новые первыми.</summary>
    Task<IReadOnlyList<StockMovement>> GetMovementsForItemAsync(int itemId, int take, CancellationToken ct = default);

    /// <summary>Активные позиции, у которых суммарный остаток ниже минимума.</summary>
    Task<IReadOnlyList<ItemStock>> GetItemsBelowMinimumAsync(CancellationToken ct = default);

    /// <summary>Сохраняет все изменения одной транзакцией.</summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Доступ к данным справочников: номенклатура, склады, места хранения.</summary>
public interface ICatalogRepository
{
    Task<Item?> GetItemAsync(int id, CancellationToken ct = default);

    Task<bool> SkuExistsAsync(string sku, CancellationToken ct = default);

    Task<IReadOnlyList<Item>> ListItemsAsync(string? search, CancellationToken ct = default);

    void AddItem(Item item);

    Task<Warehouse?> GetWarehouseAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<Warehouse>> ListWarehousesAsync(CancellationToken ct = default);

    void AddWarehouse(Warehouse warehouse);

    Task<bool> LocationCodeExistsAsync(string code, CancellationToken ct = default);

    void AddLocation(StorageLocation location);

    Task SaveChangesAsync(CancellationToken ct = default);
}
