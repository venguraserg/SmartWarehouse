using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.Application.Interfaces;

public interface IStockService
{
    /// <summary>
    /// Создаёт и проводит документ: проверяет все строки, затем одной транзакцией
    /// меняет остатки и записывает строки в журнал. Если хоть одна строка ошибочна — не меняется ничего.
    /// </summary>
    Task<DocumentDto> PostDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default);

    Task<DocumentDto> GetDocumentAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<DocumentSummaryDto>> ListDocumentsAsync(MovementType? type, int take = 100, CancellationToken ct = default);

    Task<IReadOnlyList<StockBalanceDto>> GetBalancesAsync(int itemId, CancellationToken ct = default);

    Task<IReadOnlyList<MovementDto>> GetMovementsAsync(int itemId, int take = 100, CancellationToken ct = default);

    /// <summary>Позиции с остатком ниже минимума.</summary>
    Task<IReadOnlyList<LowStockItemDto>> GetBelowMinimumAsync(CancellationToken ct = default);
}

public interface ICatalogService
{
    Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken ct = default);

    Task<CategoryDto> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<UnitDto>> ListUnitsAsync(CancellationToken ct = default);

    Task<UnitDto> CreateUnitAsync(CreateUnitRequest request, CancellationToken ct = default);

    Task<ItemDto> CreateItemAsync(CreateItemRequest request, CancellationToken ct = default);

    Task<ItemDto> GetItemAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<ItemDto>> ListItemsAsync(string? search, int? categoryId, CancellationToken ct = default);
}

public interface ILocationService
{
    Task<WarehouseDto> CreateWarehouseAsync(CreateWarehouseRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<WarehouseDto>> ListWarehousesAsync(CancellationToken ct = default);

    Task<LocationDto> AddLocationAsync(int warehouseId, CreateLocationRequest request, CancellationToken ct = default);

    /// <summary>Дерево мест хранения склада.</summary>
    Task<IReadOnlyList<LocationNodeDto>> GetLocationTreeAsync(int warehouseId, CancellationToken ct = default);

    /// <summary>Поиск места по полному коду (содержимое QR-метки).</summary>
    Task<LocationDto> GetByCodeAsync(string fullCode, CancellationToken ct = default);

    /// <summary>Что лежит в месте хранения.</summary>
    Task<IReadOnlyList<LocationContentDto>> GetContentAsync(int locationId, CancellationToken ct = default);
}
