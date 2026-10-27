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

    /// <summary>Что лежит на складе по факту: остатки по местам с фильтрами.</summary>
    Task<StockSearchResult> SearchStockAsync(StockQuery query, CancellationToken ct = default);

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

    Task<ItemDto> UpdateItemAsync(int id, UpdateItemRequest request, CancellationToken ct = default);

    /// <summary>Следующий свободный артикул вида «ЗЧ-000001» — подсказка для новой позиции.</summary>
    Task<string> SuggestSkuAsync(CancellationToken ct = default);

    /// <summary>Загружает или заменяет фото позиции. Формат определяется по содержимому файла.</summary>
    Task<ItemDto> SetImageAsync(int itemId, byte[] data, CancellationToken ct = default);

    Task<ItemImageDto> GetImageAsync(int itemId, CancellationToken ct = default);

    Task DeleteImageAsync(int itemId, CancellationToken ct = default);

    Task<IReadOnlyList<EquipmentDto>> ListEquipmentAsync(CancellationToken ct = default);

    Task<EquipmentDto> CreateEquipmentAsync(CreateEquipmentRequest request, CancellationToken ct = default);
}

public interface IEmployeeService
{
    /// <summary>Список сотрудников; поиск по ФИО, табельному номеру, должности, подразделению.</summary>
    Task<IReadOnlyList<EmployeeDto>> ListAsync(string? search, bool includeInactive, CancellationToken ct = default);

    Task<EmployeeDto> GetAsync(int id, CancellationToken ct = default);

    Task<EmployeeDto> CreateAsync(SaveEmployeeRequest request, CancellationToken ct = default);

    Task<EmployeeDto> UpdateAsync(int id, SaveEmployeeRequest request, CancellationToken ct = default);

    /// <summary>История подотчёта: выдачи, списания, возвраты.</summary>
    Task<IReadOnlyList<EmployeeMovementDto>> GetHistoryAsync(int id, int take = 200, CancellationToken ct = default);

    /// <summary>Что числится за сотрудниками. Фильтры: сотрудник, позиция, текст.</summary>
    Task<IReadOnlyList<CustodyRowDto>> ListCustodyAsync(int? employeeId = null, int? itemId = null, string? search = null, CancellationToken ct = default);
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
