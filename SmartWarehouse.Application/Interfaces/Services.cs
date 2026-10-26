using SmartWarehouse.Application.DTOs;

namespace SmartWarehouse.Application.Interfaces;

public interface IStockService
{
    /// <summary>Выполняет складскую операцию: проверяет правила, обновляет остатки и пишет запись в журнал.</summary>
    Task<MovementDto> RegisterAsync(MovementRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<StockBalanceDto>> GetBalancesAsync(int itemId, CancellationToken ct = default);

    Task<IReadOnlyList<MovementDto>> GetMovementsAsync(int itemId, int take = 100, CancellationToken ct = default);

    /// <summary>Список для закупки: позиции с остатком ниже минимума.</summary>
    Task<IReadOnlyList<LowStockItemDto>> GetBelowMinimumAsync(CancellationToken ct = default);
}

public interface ICatalogService
{
    Task<ItemDto> CreateItemAsync(CreateItemRequest request, CancellationToken ct = default);

    Task<ItemDto> GetItemAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<ItemDto>> ListItemsAsync(string? search, CancellationToken ct = default);

    Task<WarehouseDto> CreateWarehouseAsync(CreateWarehouseRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<WarehouseDto>> ListWarehousesAsync(CancellationToken ct = default);

    Task<LocationDto> AddLocationAsync(int warehouseId, CreateLocationRequest request, CancellationToken ct = default);
}
