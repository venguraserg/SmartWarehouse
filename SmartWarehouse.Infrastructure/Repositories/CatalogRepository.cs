using Microsoft.EntityFrameworkCore;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Infrastructure.Data;

namespace SmartWarehouse.Infrastructure.Repositories;

public class CatalogRepository(WarehouseDbContext db) : ICatalogRepository
{
    private const int MaxListSize = 500;

    // ---------- Категории ----------

    public async Task<IReadOnlyList<Category>> ListCategoriesAsync(CancellationToken ct = default) =>
        await db.Categories.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);

    public Task<bool> CategoryExistsAsync(int id, CancellationToken ct = default) =>
        db.Categories.AnyAsync(x => x.Id == id, ct);

    public Task<bool> CategoryNameExistsAsync(string name, CancellationToken ct = default)
    {
        var lower = name.ToLower();
        return db.Categories.AnyAsync(x => x.Name.ToLower() == lower, ct);
    }

    public void AddCategory(Category category) => db.Categories.Add(category);

    // ---------- Единицы измерения ----------

    public async Task<IReadOnlyList<UnitOfMeasure>> ListUnitsAsync(CancellationToken ct = default) =>
        await db.Units.AsNoTracking().OrderBy(x => x.Code).ToListAsync(ct);

    public Task<bool> UnitExistsAsync(int id, CancellationToken ct = default) =>
        db.Units.AnyAsync(x => x.Id == id, ct);

    public Task<bool> UnitCodeExistsAsync(string code, CancellationToken ct = default)
    {
        var lower = code.ToLower();
        return db.Units.AnyAsync(x => x.Code.ToLower() == lower, ct);
    }

    public void AddUnit(UnitOfMeasure unit) => db.Units.Add(unit);

    // ---------- Номенклатура ----------

    public Task<Item?> GetItemAsync(int id, CancellationToken ct = default) =>
        db.Items
            .AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.Unit)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<bool> SkuExistsAsync(string sku, CancellationToken ct = default) =>
        db.Items.AnyAsync(x => x.Sku == sku, ct);

    public async Task<IReadOnlyList<Item>> ListItemsAsync(string? search, int? categoryId, CancellationToken ct = default)
    {
        var query = db.Items.AsNoTracking().Include(x => x.Category).Include(x => x.Unit).AsQueryable();

        if (!string.IsNullOrEmpty(search))
        {
            var pattern = $"%{search}%";
            query = query.Where(x => EF.Functions.ILike(x.Name, pattern) || EF.Functions.ILike(x.Sku, pattern));
        }

        if (categoryId is not null)
            query = query.Where(x => x.CategoryId == categoryId);

        return await query.OrderBy(x => x.Name).Take(MaxListSize).ToListAsync(ct);
    }

    public void AddItem(Item item) => db.Items.Add(item);

    public Task<Item?> GetItemForUpdateAsync(int id, CancellationToken ct = default) =>
        db.Items.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<bool> ItemHasMovementsAsync(int id, CancellationToken ct = default) =>
        db.Movements.AnyAsync(x => x.ItemId == id, ct);

    // ---------- Фото ----------

    public Task<ItemImage?> GetImageAsync(int itemId, CancellationToken ct = default) =>
        db.ItemImages.FirstOrDefaultAsync(x => x.ItemId == itemId, ct);

    public void AddImage(ItemImage image) => db.ItemImages.Add(image);

    public void RemoveImage(ItemImage image) => db.ItemImages.Remove(image);

    // ---------- Оборудование ----------

    public async Task<IReadOnlyList<Equipment>> ListEquipmentAsync(CancellationToken ct = default) =>
        await db.Equipment.AsNoTracking().OrderBy(x => x.Code).ToListAsync(ct);

    public Task<bool> EquipmentExistsAsync(int id, CancellationToken ct = default) =>
        db.Equipment.AnyAsync(x => x.Id == id, ct);

    public Task<bool> EquipmentCodeExistsAsync(string code, CancellationToken ct = default) =>
        db.Equipment.AnyAsync(x => x.Code == code, ct);

    public void AddEquipment(Equipment equipment) => db.Equipment.Add(equipment);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
