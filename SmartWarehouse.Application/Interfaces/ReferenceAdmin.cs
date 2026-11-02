using SmartWarehouse.Domain.Entities;

namespace SmartWarehouse.Application.Interfaces;

/// <summary>Данные для правки справочников администратором (сущности отслеживаются — изменения сохраняются).</summary>
public interface IReferenceAdminRepository
{
    // ---------- Склады и места ----------

    Task<Warehouse?> GetWarehouseAsync(int id, CancellationToken ct = default);

    Task<bool> WarehouseCodeExistsAsync(string code, int exceptId, CancellationToken ct = default);

    /// <summary>Все места склада (для пересчёта полных кодов поддерева).</summary>
    Task<List<StorageLocation>> GetWarehouseLocationsAsync(int warehouseId, CancellationToken ct = default);

    Task<StorageLocation?> GetLocationAsync(int id, CancellationToken ct = default);

    /// <summary>Какие из полных кодов уже заняты местами, кроме перечисленных.</summary>
    Task<IReadOnlyList<string>> FindFullCodesAsync(IReadOnlyCollection<string> codes, IReadOnlyCollection<int> exceptIds, CancellationToken ct = default);

    Task<bool> LocationHasStockAsync(int locationId, CancellationToken ct = default);

    /// <summary>Были ли движения через место (документы на него ссылаются).</summary>
    Task<bool> LocationHasMovementsAsync(int locationId, CancellationToken ct = default);

    // ---------- Категории ----------

    Task<Category?> GetCategoryAsync(int id, CancellationToken ct = default);

    Task<bool> CategoryNameExistsAsync(string name, int exceptId, CancellationToken ct = default);

    /// <summary>Убирает категорию у позиций; возвращает, у скольких.</summary>
    Task<int> ClearCategoryAsync(int categoryId, CancellationToken ct = default);

    // ---------- Единицы ----------

    Task<UnitOfMeasure?> GetUnitAsync(int id, CancellationToken ct = default);

    Task<bool> UnitCodeExistsAsync(string code, int exceptId, CancellationToken ct = default);

    Task<int> CountItemsWithUnitAsync(int unitId, CancellationToken ct = default);

    /// <summary>Есть ли у позиций с этой единицей дробные количества (в остатках, подотчёте, движениях, актах, минимумах).</summary>
    Task<bool> UnitHasFractionalQuantitiesAsync(int unitId, CancellationToken ct = default);

    // ---------- Оборудование ----------

    Task<Equipment?> GetEquipmentAsync(int id, CancellationToken ct = default);

    Task<bool> EquipmentCodeExistsAsync(string code, int exceptId, CancellationToken ct = default);

    Task<IReadOnlyList<(int Id, int? ParentId)>> ListEquipmentHierarchyAsync(CancellationToken ct = default);

    /// <summary>Указано ли оборудование в документах или актах.</summary>
    Task<bool> EquipmentIsUsedAsync(int equipmentId, CancellationToken ct = default);

    // ---------- Общее ----------

    void Remove(object entity);

    Task SaveChangesAsync(CancellationToken ct = default);
}
