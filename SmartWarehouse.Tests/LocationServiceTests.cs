using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Tests;

public class LocationServiceTests
{
    private readonly FakeLocationRepository _repo = new();
    private readonly LocationService _service;

    public LocationServiceTests() => _service = new LocationService(_repo);

    private async Task<int> WarehouseAsync(string code = "ск1") =>
        (await _service.CreateWarehouseAsync(new CreateWarehouseRequest(code, "Склад запчастей", null))).Id;

    private Task<LocationDto> AddAsync(int warehouseId, int? parentId, LocationType type, string code) =>
        _service.AddLocationAsync(warehouseId, new CreateLocationRequest(parentId, type, code, null));

    [Fact]
    public async Task Полный_код_собирается_из_всех_уровней_в_верхнем_регистре()
    {
        var wh = await WarehouseAsync();
        var rack = await AddAsync(wh, null, LocationType.Rack, "a");
        var shelf = await AddAsync(wh, rack.Id, LocationType.Shelf, "01");
        var cell = await AddAsync(wh, shelf.Id, LocationType.Cell, "03");

        Assert.Equal("СК1-A", rack.FullCode);
        Assert.Equal("СК1-A-01-03", cell.FullCode);
        Assert.Equal(shelf.Id, cell.ParentId);
    }

    [Fact]
    public async Task Уровни_можно_пропускать()
    {
        var wh = await WarehouseAsync();
        var rack = await AddAsync(wh, null, LocationType.Rack, "B");
        var cell = await AddAsync(wh, rack.Id, LocationType.Cell, "1");

        Assert.Equal("СК1-B-1", cell.FullCode);
    }

    [Theory]
    [InlineData(LocationType.Shelf, LocationType.Shelf)]
    [InlineData(LocationType.Shelf, LocationType.Rack)]
    [InlineData(LocationType.Cell, LocationType.Zone)]
    public async Task Вложенность_только_вниз(LocationType parentType, LocationType childType)
    {
        var wh = await WarehouseAsync();
        var parent = await AddAsync(wh, null, parentType, "P");

        await Assert.ThrowsAsync<DomainException>(() => AddAsync(wh, parent.Id, childType, "C"));
    }

    [Fact]
    public async Task В_место_с_товаром_нельзя_добавить_вложенное()
    {
        var wh = await WarehouseAsync();
        var shelf = await AddAsync(wh, null, LocationType.Shelf, "01");
        _repo.LocationsWithStock.Add(shelf.Id);

        await Assert.ThrowsAsync<DomainException>(() => AddAsync(wh, shelf.Id, LocationType.Cell, "01"));
    }

    [Fact]
    public async Task Повтор_кода_на_одном_уровне_запрещён()
    {
        var wh = await WarehouseAsync();
        await AddAsync(wh, null, LocationType.Rack, "A");

        await Assert.ThrowsAsync<DomainException>(() => AddAsync(wh, null, LocationType.Rack, "a"));
    }

    [Fact]
    public async Task Родитель_с_другого_склада_запрещён()
    {
        var wh1 = await WarehouseAsync("СК1");
        var wh2 = await WarehouseAsync("СК2");
        var rack = await AddAsync(wh1, null, LocationType.Rack, "A");

        await Assert.ThrowsAsync<DomainException>(() => AddAsync(wh2, rack.Id, LocationType.Shelf, "01"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("A-1")]
    [InlineData("A 1")]
    [InlineData("A1234567890123456")] // 17 символов
    public async Task Недопустимый_код_отклоняется(string code)
    {
        var wh = await WarehouseAsync();

        await Assert.ThrowsAsync<DomainException>(() => AddAsync(wh, null, LocationType.Rack, code));
    }

    [Fact]
    public async Task Дубликат_кода_склада_запрещён()
    {
        await WarehouseAsync("СК1");

        await Assert.ThrowsAsync<DomainException>(() => WarehouseAsync("ск1"));
    }

    [Fact]
    public async Task Дерево_строится_по_уровням()
    {
        var wh = await WarehouseAsync();
        var rack = await AddAsync(wh, null, LocationType.Rack, "A");
        var shelf = await AddAsync(wh, rack.Id, LocationType.Shelf, "01");
        await AddAsync(wh, shelf.Id, LocationType.Cell, "02");
        await AddAsync(wh, shelf.Id, LocationType.Cell, "01");

        var root = Assert.Single(await _service.GetLocationTreeAsync(wh));
        var shelfNode = Assert.Single(root.Children);
        Assert.Equal(new[] { "СК1-A-01-01", "СК1-A-01-02" }, shelfNode.Children.Select(c => c.FullCode));
    }

    [Fact]
    public async Task Поиск_по_коду_из_QR_не_зависит_от_регистра()
    {
        var wh = await WarehouseAsync();
        var rack = await AddAsync(wh, null, LocationType.Rack, "A");

        var found = await _service.GetByCodeAsync(" ск1-a ");
        Assert.Equal(rack.Id, found.Id);

        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetByCodeAsync("СК1-Z"));
    }
}

/// <summary>Репозиторий складов и мест хранения в памяти.</summary>
internal class FakeLocationRepository : ILocationRepository
{
    public List<Warehouse> Warehouses { get; } = [];
    public List<StorageLocation> Locations { get; } = [];
    public HashSet<int> LocationsWithStock { get; } = [];

    private readonly List<object> _pending = [];

    public Task<Warehouse?> GetWarehouseAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Warehouses.FirstOrDefault(x => x.Id == id));

    public Task<IReadOnlyList<Warehouse>> ListWarehousesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Warehouse>>(Warehouses.OrderBy(x => x.Code).ToList());

    public Task<bool> WarehouseCodeExistsAsync(string code, CancellationToken ct = default) =>
        Task.FromResult(Warehouses.Any(x => x.Code == code));

    public void AddWarehouse(Warehouse warehouse) => _pending.Add(warehouse);

    public Task<StorageLocation?> GetLocationAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Locations.FirstOrDefault(x => x.Id == id));

    public Task<StorageLocation?> GetLocationByFullCodeAsync(string fullCode, CancellationToken ct = default) =>
        Task.FromResult(Locations.FirstOrDefault(x => x.FullCode == fullCode));

    public Task<IReadOnlyList<StorageLocation>> GetLocationsAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<StorageLocation>>(Locations.Where(x => ids.Contains(x.Id)).ToList());

    public Task<IReadOnlyList<StorageLocation>> ListLocationsAsync(int warehouseId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<StorageLocation>>(Locations.Where(x => x.WarehouseId == warehouseId).ToList());

    public Task<bool> FullCodeExistsAsync(string fullCode, CancellationToken ct = default) =>
        Task.FromResult(Locations.Any(x => x.FullCode == fullCode));

    public Task<bool> LocationHasStockAsync(int locationId, CancellationToken ct = default) =>
        Task.FromResult(LocationsWithStock.Contains(locationId));

    public Task<IReadOnlyList<StockBalance>> GetLocationContentAsync(int locationId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<StockBalance>>([]);

    public void AddLocation(StorageLocation location) => _pending.Add(location);

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var entity in _pending)
        {
            switch (entity)
            {
                case Warehouse w:
                    w.Id = Warehouses.Count + 1;
                    Warehouses.Add(w);
                    break;
                case StorageLocation l:
                    l.Id = Locations.Count + 100;
                    Locations.Add(l);
                    break;
            }
        }
        _pending.Clear();
        return Task.CompletedTask;
    }
}
