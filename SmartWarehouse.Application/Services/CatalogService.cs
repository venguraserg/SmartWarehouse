using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Application.Services;

/// <summary>Справочники: категории, единицы измерения, номенклатура.</summary>
public class CatalogService(ICatalogRepository repo) : ICatalogService
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

    private static CategoryDto ToDto(Category c) => new(c.Id, c.Name, c.Description);

    private static UnitDto ToDto(UnitOfMeasure u) => new(u.Id, u.Code, u.Name, u.IsFractional);

    private static ItemDto ToDto(Item i) => new(
        i.Id, i.Sku, i.Name, i.CategoryId, i.Category?.Name, i.UnitId, i.Unit.Code,
        i.MinStock, i.MaxStock, i.Description, i.IsActive);
}
