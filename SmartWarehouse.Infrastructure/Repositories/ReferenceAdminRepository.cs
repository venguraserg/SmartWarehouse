using Microsoft.EntityFrameworkCore;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Infrastructure.Data;

namespace SmartWarehouse.Infrastructure.Repositories;

public class ReferenceAdminRepository(WarehouseDbContext db) : IReferenceAdminRepository
{
    public Task<Warehouse?> GetWarehouseAsync(int id, CancellationToken ct = default) =>
        db.Warehouses.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<bool> WarehouseCodeExistsAsync(string code, int exceptId, CancellationToken ct = default) =>
        db.Warehouses.AnyAsync(x => x.Code == code && x.Id != exceptId, ct);

    public Task<List<StorageLocation>> GetWarehouseLocationsAsync(int warehouseId, CancellationToken ct = default) =>
        db.Locations.Where(x => x.WarehouseId == warehouseId).ToListAsync(ct);

    public Task<StorageLocation?> GetLocationAsync(int id, CancellationToken ct = default) =>
        db.Locations.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<string>> FindFullCodesAsync(
        IReadOnlyCollection<string> codes, IReadOnlyCollection<int> exceptIds, CancellationToken ct = default)
    {
        var list = codes.ToList();
        var except = exceptIds.ToList();
        return await db.Locations
            .Where(x => list.Contains(x.FullCode) && !except.Contains(x.Id))
            .Select(x => x.FullCode)
            .ToListAsync(ct);
    }

    public Task<bool> LocationHasStockAsync(int locationId, CancellationToken ct = default) =>
        db.Balances.AnyAsync(x => x.LocationId == locationId && x.Quantity > 0, ct);

    public Task<bool> LocationHasMovementsAsync(int locationId, CancellationToken ct = default) =>
        db.Movements.AnyAsync(x => x.FromLocationId == locationId || x.ToLocationId == locationId, ct);

    public Task<Category?> GetCategoryAsync(int id, CancellationToken ct = default) =>
        db.Categories.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<bool> CategoryNameExistsAsync(string name, int exceptId, CancellationToken ct = default) =>
        db.Categories.AnyAsync(x => x.Name.ToLower() == name.ToLower() && x.Id != exceptId, ct);

    public Task<int> ClearCategoryAsync(int categoryId, CancellationToken ct = default) =>
        db.Items.Where(x => x.CategoryId == categoryId).ExecuteUpdateAsync(s => s.SetProperty(x => x.CategoryId, (int?)null), ct);

    public Task<UnitOfMeasure?> GetUnitAsync(int id, CancellationToken ct = default) =>
        db.Units.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<bool> UnitCodeExistsAsync(string code, int exceptId, CancellationToken ct = default) =>
        db.Units.AnyAsync(x => x.Code.ToLower() == code.ToLower() && x.Id != exceptId, ct);

    public Task<int> CountItemsWithUnitAsync(int unitId, CancellationToken ct = default) =>
        db.Items.CountAsync(x => x.UnitId == unitId, ct);

    public async Task<bool> UnitHasFractionalQuantitiesAsync(int unitId, CancellationToken ct = default) =>
        await db.Items.AnyAsync(x => x.UnitId == unitId
                && (x.MinStock != Math.Floor(x.MinStock) || (x.MaxStock != null && x.MaxStock != Math.Floor(x.MaxStock.Value))), ct)
            || await db.Balances.AnyAsync(x => x.Item.UnitId == unitId && x.Quantity != Math.Floor(x.Quantity), ct)
            || await db.EmployeeBalances.AnyAsync(x => x.Item.UnitId == unitId && x.Quantity != Math.Floor(x.Quantity), ct)
            || await db.Movements.AnyAsync(x => x.Item.UnitId == unitId && x.Quantity != Math.Floor(x.Quantity), ct)
            || await db.WriteOffActLines.AnyAsync(x => x.Item.UnitId == unitId && x.Quantity != Math.Floor(x.Quantity), ct);

    public Task<Equipment?> GetEquipmentAsync(int id, CancellationToken ct = default) =>
        db.Equipment.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<bool> EquipmentCodeExistsAsync(string code, int exceptId, CancellationToken ct = default) =>
        db.Equipment.AnyAsync(x => x.Code.ToLower() == code.ToLower() && x.Id != exceptId, ct);

    public async Task<IReadOnlyList<(int Id, int? ParentId)>> ListEquipmentHierarchyAsync(CancellationToken ct = default) =>
        (await db.Equipment.AsNoTracking().Select(x => new { x.Id, x.ParentId }).ToListAsync(ct))
            .Select(x => (x.Id, x.ParentId))
            .ToList();

    public async Task<bool> EquipmentIsUsedAsync(int equipmentId, CancellationToken ct = default) =>
        await db.Documents.AnyAsync(x => x.EquipmentId == equipmentId, ct)
        || await db.WriteOffActLines.AnyAsync(x => x.EquipmentId == equipmentId, ct);

    public void Remove(object entity) => db.Remove(entity);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
