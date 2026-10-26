using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Application.Services;

/// <summary>Справочники: категории, единицы измерения, номенклатура.</summary>
public class CatalogService(ICatalogRepository repo, TimeProvider clock) : ICatalogService
{
    // ---------- Категории ----------

    public async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken ct = default) =>
        (await repo.ListCategoriesAsync(ct)).Select(ToDto).ToList();

    public async Task<CategoryDto> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken ct = default)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)) throw new DomainException("Название категории обязательно.");

        if (await repo.CategoryNameExistsAsync(name, ct))
            throw new DomainException($"Категория «{name}» уже существует.");

        var category = new Category { Name = name, Description = request.Description?.Trim() };
        repo.AddCategory(category);
        await repo.SaveChangesAsync(ct);
        return ToDto(category);
    }

    // ---------- Единицы измерения ----------

    public async Task<IReadOnlyList<UnitDto>> ListUnitsAsync(CancellationToken ct = default) =>
        (await repo.ListUnitsAsync(ct)).Select(ToDto).ToList();

    public async Task<UnitDto> CreateUnitAsync(CreateUnitRequest request, CancellationToken ct = default)
    {
        var code = request.Code?.Trim();
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(code)) throw new DomainException("Обозначение единицы обязательно.");
        if (string.IsNullOrEmpty(name)) throw new DomainException("Название единицы обязательно.");

        if (await repo.UnitCodeExistsAsync(code, ct))
            throw new DomainException($"Единица «{code}» уже существует.");

        var unit = new UnitOfMeasure { Code = code, Name = name, IsFractional = request.IsFractional };
        repo.AddUnit(unit);
        await repo.SaveChangesAsync(ct);
        return ToDto(unit);
    }

    // ---------- Номенклатура ----------

    public async Task<ItemDto> CreateItemAsync(CreateItemRequest request, CancellationToken ct = default)
    {
        var sku = request.Sku?.Trim();
        var name = request.Name?.Trim();

        if (string.IsNullOrEmpty(sku)) throw new DomainException("Артикул обязателен.");
        if (string.IsNullOrEmpty(name)) throw new DomainException("Наименование обязательно.");
        if (request.MinStock < 0) throw new DomainException("Минимальный остаток не может быть отрицательным.");
        if (request.MaxStock is { } max && max < request.MinStock)
            throw new DomainException("Максимальный остаток не может быть меньше минимального.");

        if (!await repo.UnitExistsAsync(request.UnitId, ct))
            throw new NotFoundException($"Единица измерения {request.UnitId} не найдена.");

        if (request.CategoryId is int categoryId && !await repo.CategoryExistsAsync(categoryId, ct))
            throw new NotFoundException($"Категория {categoryId} не найдена.");

        if (await repo.SkuExistsAsync(sku, ct))
            throw new DomainException($"Позиция с артикулом «{sku}» уже существует.");

        var item = new Item
        {
            Sku = sku,
            Name = name,
            CategoryId = request.CategoryId,
            UnitId = request.UnitId,
            MinStock = request.MinStock,
            MaxStock = request.MaxStock,
            Description = request.Description?.Trim()
        };

        repo.AddItem(item);
        await repo.SaveChangesAsync(ct);

        // Перечитываем, чтобы вернуть названия категории и единицы.
        return await GetItemAsync(item.Id, ct);
    }

    public async Task<ItemDto> GetItemAsync(int id, CancellationToken ct = default)
    {
        var item = await repo.GetItemAsync(id, ct)
            ?? throw new NotFoundException($"Позиция {id} не найдена.");
        return ToDto(item);
    }

    public async Task<IReadOnlyList<ItemDto>> ListItemsAsync(string? search, int? categoryId, CancellationToken ct = default)
    {
        var items = await repo.ListItemsAsync(search?.Trim(), categoryId, ct);
        return items.Select(ToDto).ToList();
    }

    public async Task<ItemDto> UpdateItemAsync(int id, UpdateItemRequest request, CancellationToken ct = default)
    {
        var item = await repo.GetItemForUpdateAsync(id, ct)
            ?? throw new NotFoundException($"Позиция {id} не найдена.");

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)) throw new DomainException("Наименование обязательно.");
        if (request.MinStock < 0) throw new DomainException("Минимальный остаток не может быть отрицательным.");
        if (request.MaxStock is { } max && max < request.MinStock)
            throw new DomainException("Максимальный остаток не может быть меньше минимального.");

        if (request.CategoryId is int categoryId && !await repo.CategoryExistsAsync(categoryId, ct))
            throw new NotFoundException($"Категория {categoryId} не найдена.");

        if (request.UnitId != item.UnitId)
        {
            if (!await repo.UnitExistsAsync(request.UnitId, ct))
                throw new NotFoundException($"Единица измерения {request.UnitId} не найдена.");
            if (await repo.ItemHasMovementsAsync(id, ct))
                throw new DomainException("По позиции уже были движения — единицу измерения изменить нельзя.");
        }

        item.Name = name;
        item.CategoryId = request.CategoryId;
        item.UnitId = request.UnitId;
        item.MinStock = request.MinStock;
        item.MaxStock = request.MaxStock;
        item.Description = request.Description?.Trim();
        item.IsActive = request.IsActive;

        await repo.SaveChangesAsync(ct);
        return await GetItemAsync(id, ct);
    }

    // ---------- Фото ----------

    public async Task<ItemDto> SetImageAsync(int itemId, byte[] data, CancellationToken ct = default)
    {
        var item = await repo.GetItemForUpdateAsync(itemId, ct)
            ?? throw new NotFoundException($"Позиция {itemId} не найдена.");

        var contentType = ImageRules.DetectContentType(data);

        var image = await repo.GetImageAsync(itemId, ct);
        if (image is null)
        {
            image = new ItemImage { ItemId = itemId };
            repo.AddImage(image);
        }

        image.ContentType = contentType;
        image.Data = data;
        item.ImageUpdatedAt = clock.GetUtcNow();

        await repo.SaveChangesAsync(ct);
        return await GetItemAsync(itemId, ct);
    }

    public async Task<ItemImageDto> GetImageAsync(int itemId, CancellationToken ct = default)
    {
        var image = await repo.GetImageAsync(itemId, ct)
            ?? throw new NotFoundException($"У позиции {itemId} нет фото.");
        return new ItemImageDto(image.Data, image.ContentType);
    }

    public async Task DeleteImageAsync(int itemId, CancellationToken ct = default)
    {
        var item = await repo.GetItemForUpdateAsync(itemId, ct)
            ?? throw new NotFoundException($"Позиция {itemId} не найдена.");

        var image = await repo.GetImageAsync(itemId, ct);
        if (image is not null)
            repo.RemoveImage(image);

        item.ImageUpdatedAt = null;
        await repo.SaveChangesAsync(ct);
    }

    // ---------- Оборудование ----------

    public async Task<IReadOnlyList<EquipmentDto>> ListEquipmentAsync(CancellationToken ct = default) =>
        (await repo.ListEquipmentAsync(ct)).Select(e => new EquipmentDto(e.Id, e.ParentId, e.Code, e.Name)).ToList();

    public async Task<EquipmentDto> CreateEquipmentAsync(CreateEquipmentRequest request, CancellationToken ct = default)
    {
        var code = request.Code?.Trim();
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(code)) throw new DomainException("Код оборудования обязателен.");
        if (string.IsNullOrEmpty(name)) throw new DomainException("Наименование оборудования обязательно.");

        if (request.ParentId is int parentId && !await repo.EquipmentExistsAsync(parentId, ct))
            throw new NotFoundException($"Родительское оборудование {parentId} не найдено.");

        if (await repo.EquipmentCodeExistsAsync(code, ct))
            throw new DomainException($"Оборудование с кодом «{code}» уже существует.");

        var equipment = new Equipment { ParentId = request.ParentId, Code = code, Name = name };
        repo.AddEquipment(equipment);
        await repo.SaveChangesAsync(ct);
        return new EquipmentDto(equipment.Id, equipment.ParentId, equipment.Code, equipment.Name);
    }

    private static CategoryDto ToDto(Category c) => new(c.Id, c.Name, c.Description);

    private static UnitDto ToDto(UnitOfMeasure u) => new(u.Id, u.Code, u.Name, u.IsFractional);

    private static ItemDto ToDto(Item i) => new(
        i.Id, i.Sku, i.Name, i.CategoryId, i.Category?.Name, i.UnitId, i.Unit.Code,
        i.MinStock, i.MaxStock, i.Description, i.IsActive, i.ImageUpdatedAt?.ToUnixTimeMilliseconds());
}
