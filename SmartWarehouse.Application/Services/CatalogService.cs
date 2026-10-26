using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Application.Services;

/// <summary>Справочники: номенклатура, склады, места хранения.</summary>
public class CatalogService(ICatalogRepository repo) : ICatalogService
{
    public async Task<ItemDto> CreateItemAsync(CreateItemRequest request, CancellationToken ct = default)
    {
        var sku = request.Sku?.Trim();
        var name = request.Name?.Trim();

        if (string.IsNullOrEmpty(sku)) throw new DomainException("Артикул обязателен.");
        if (string.IsNullOrEmpty(name)) throw new DomainException("Наименование обязательно.");
        if (request.MinStock < 0) throw new DomainException("Минимальный остаток не может быть отрицательным.");
        if (request.MaxStock is { } max && max < request.MinStock)
            throw new DomainException("Максимальный остаток не может быть меньше минимального.");

        if (await repo.SkuExistsAsync(sku, ct))
            throw new DomainException($"Позиция с артикулом «{sku}» уже существует.");

        var item = new Item
        {
            Sku = sku,
            Name = name,
            Category = request.Category?.Trim(),
            Unit = string.IsNullOrWhiteSpace(request.Unit) ? "шт" : request.Unit.Trim(),
            MinStock = request.MinStock,
            MaxStock = request.MaxStock,
            Description = request.Description?.Trim()
        };

        repo.AddItem(item);
        await repo.SaveChangesAsync(ct);
        return ToDto(item);
    }

    public async Task<ItemDto> GetItemAsync(int id, CancellationToken ct = default)
    {
        var item = await repo.GetItemAsync(id, ct)
            ?? throw new NotFoundException($"Позиция {id} не найдена.");
        return ToDto(item);
    }

    public async Task<IReadOnlyList<ItemDto>> ListItemsAsync(string? search, CancellationToken ct = default)
    {
        var items = await repo.ListItemsAsync(search?.Trim(), ct);
        return items.Select(ToDto).ToList();
    }

    public async Task<WarehouseDto> CreateWarehouseAsync(CreateWarehouseRequest request, CancellationToken ct = default)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)) throw new DomainException("Название склада обязательно.");

        var warehouse = new Warehouse { Name = name, Description = request.Description?.Trim() };
        repo.AddWarehouse(warehouse);
        await repo.SaveChangesAsync(ct);
        return ToDto(warehouse);
    }

    public async Task<IReadOnlyList<WarehouseDto>> ListWarehousesAsync(CancellationToken ct = default)
    {
        var warehouses = await repo.ListWarehousesAsync(ct);
        return warehouses.Select(ToDto).ToList();
    }

    public async Task<LocationDto> AddLocationAsync(int warehouseId, CreateLocationRequest request, CancellationToken ct = default)
    {
        var warehouse = await repo.GetWarehouseAsync(warehouseId, ct)
            ?? throw new NotFoundException($"Склад {warehouseId} не найден.");

        var code = request.Code?.Trim();
        if (string.IsNullOrEmpty(code)) throw new DomainException("Код места хранения обязателен.");

        if (await repo.LocationCodeExistsAsync(code, ct))
            throw new DomainException($"Место хранения с кодом «{code}» уже существует.");

        var location = new StorageLocation { WarehouseId = warehouse.Id, Code = code, Name = request.Name?.Trim() };
        repo.AddLocation(location);
        await repo.SaveChangesAsync(ct);
        return ToDto(location);
    }

    private static ItemDto ToDto(Item i) => new(
        i.Id, i.Sku, i.Name, i.Category, i.Unit, i.MinStock, i.MaxStock, i.Description, i.IsActive);

    private static LocationDto ToDto(StorageLocation l) => new(l.Id, l.WarehouseId, l.Code, l.Name);

    private static WarehouseDto ToDto(Warehouse w) => new(
        w.Id, w.Name, w.Description, w.Locations.Select(ToDto).ToList());
}
