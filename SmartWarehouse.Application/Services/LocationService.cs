using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Application.Services;

/// <summary>
/// Склады и дерево мест хранения: склад → зона → стеллаж → полка → ячейка.
/// Полный код места собирается из кодов всех уровней: «СК1-A-01-03».
/// </summary>
public class LocationService(ILocationRepository repo) : ILocationService
{
    public async Task<WarehouseDto> CreateWarehouseAsync(CreateWarehouseRequest request, CancellationToken ct = default)
    {
        var code = CodeRules.NormalizePart(request.Code, "Склад");
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)) throw new DomainException("Название склада обязательно.");

        if (await repo.WarehouseCodeExistsAsync(code, ct))
            throw new DomainException($"Склад с кодом «{code}» уже существует.");

        var warehouse = new Warehouse { Code = code, Name = name, Description = request.Description?.Trim() };
        repo.AddWarehouse(warehouse);
        await repo.SaveChangesAsync(ct);
        return ToDto(warehouse);
    }

    public async Task<IReadOnlyList<WarehouseDto>> ListWarehousesAsync(CancellationToken ct = default) =>
        (await repo.ListWarehousesAsync(ct)).Select(ToDto).ToList();

    public async Task<LocationDto> AddLocationAsync(int warehouseId, CreateLocationRequest request, CancellationToken ct = default)
    {
        var warehouse = await repo.GetWarehouseAsync(warehouseId, ct)
            ?? throw new NotFoundException($"Склад {warehouseId} не найден.");

        if (!Enum.IsDefined(request.Type))
            throw new DomainException($"Неизвестный тип места хранения: {request.Type}.");

        var code = CodeRules.NormalizePart(request.Code, "Место хранения");
        var prefix = warehouse.Code;

        if (request.ParentId is int parentId)
        {
            var parent = await repo.GetLocationAsync(parentId, ct)
                ?? throw new NotFoundException($"Родительское место {parentId} не найдено.");

            if (parent.WarehouseId != warehouse.Id)
                throw new DomainException($"Место {parent.FullCode} относится к другому складу.");

            if (request.Type <= parent.Type)
                throw new DomainException(
                    $"Нельзя вложить «{TypeName(request.Type)}» в «{TypeName(parent.Type)}»: вложенность только вниз (зона → стеллаж → полка → ячейка).");

            // Товар хранится только в конечных местах: в место с остатком нельзя добавить вложенное.
            if (await repo.LocationHasStockAsync(parent.Id, ct))
                throw new DomainException($"В месте {parent.FullCode} лежит товар. Сначала переместите его, затем добавляйте вложенные места.");

            prefix = parent.FullCode;
        }

        var fullCode = $"{prefix}{CodeRules.Separator}{code}";
        if (await repo.FullCodeExistsAsync(fullCode, ct))
            throw new DomainException($"Место хранения «{fullCode}» уже существует.");

        var location = new StorageLocation
        {
            WarehouseId = warehouse.Id,
            ParentId = request.ParentId,
            Type = request.Type,
            Code = code,
            FullCode = fullCode,
            Name = request.Name?.Trim()
        };

        repo.AddLocation(location);
        await repo.SaveChangesAsync(ct);
        return ToDto(location);
    }

    public async Task<IReadOnlyList<LocationNodeDto>> GetLocationTreeAsync(int warehouseId, CancellationToken ct = default)
    {
        if (await repo.GetWarehouseAsync(warehouseId, ct) is null)
            throw new NotFoundException($"Склад {warehouseId} не найден.");

        var all = await repo.ListLocationsAsync(warehouseId, ct);
        var byParent = all.ToLookup(l => l.ParentId);

        IReadOnlyList<LocationNodeDto> Build(int? parentId) =>
            byParent[parentId]
                .OrderBy(l => l.Code)
                .Select(l => new LocationNodeDto(l.Id, l.ParentId, l.Type, l.Code, l.FullCode, l.Name, Build(l.Id)))
                .ToList();

        return Build(null);
    }

    public async Task<LocationDto> GetByCodeAsync(string fullCode, CancellationToken ct = default)
    {
        var code = fullCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var location = await repo.GetLocationByFullCodeAsync(code, ct)
            ?? throw new NotFoundException($"Место хранения «{code}» не найдено.");
        return ToDto(location);
    }

    public async Task<IReadOnlyList<LocationContentDto>> GetContentAsync(int locationId, CancellationToken ct = default)
    {
        if (await repo.GetLocationAsync(locationId, ct) is null)
            throw new NotFoundException($"Место хранения {locationId} не найдено.");

        var balances = await repo.GetLocationContentAsync(locationId, ct);
        return balances
            .Select(b => new LocationContentDto(b.ItemId, b.Item.Sku, b.Item.Name, b.Item.Unit.Code, b.Quantity,
                b.Item.ImageUpdatedAt?.ToUnixTimeMilliseconds()))
            .OrderBy(x => x.Name)
            .ToList();
    }

    internal static string TypeName(LocationType type) => type switch
    {
        LocationType.Zone => "зона",
        LocationType.Rack => "стеллаж",
        LocationType.Shelf => "полка",
        LocationType.Cell => "ячейка",
        _ => type.ToString()
    };

    private static WarehouseDto ToDto(Warehouse w) => new(w.Id, w.Code, w.Name, w.Description);

    private static LocationDto ToDto(StorageLocation l) =>
        new(l.Id, l.WarehouseId, l.ParentId, l.Type, l.Code, l.FullCode, l.Name);
}
