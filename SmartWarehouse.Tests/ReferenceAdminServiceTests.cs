using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Tests;

public class ReferenceAdminServiceTests
{
    // Склад WH1: стеллаж A (полка 01: ячейки 01, 02), стеллаж B (пустой).
    private const int RackA = 1, ShelfA1 = 2, Cell1 = 3, Cell2 = 4, RackB = 5;

    private readonly FakeReferenceAdminRepository _repo = new();
    private readonly ReferenceAdminService _service;

    public ReferenceAdminServiceTests()
    {
        _repo.Warehouses.Add(new Warehouse { Id = 1, Code = "WH1", Name = "Склад" });
        _repo.Warehouses.Add(new Warehouse { Id = 2, Code = "WH2", Name = "Второй" });
        Add(RackA, null, LocationType.Rack, "A");
        Add(ShelfA1, RackA, LocationType.Shelf, "01");
        Add(Cell1, ShelfA1, LocationType.Cell, "01");
        Add(Cell2, ShelfA1, LocationType.Cell, "02");
        Add(RackB, null, LocationType.Rack, "B");
        _service = new ReferenceAdminService(_repo);
    }

    private void Add(int id, int? parentId, LocationType type, string code)
    {
        var parent = _repo.Locations.FirstOrDefault(l => l.Id == parentId);
        _repo.Locations.Add(new StorageLocation
        {
            Id = id, WarehouseId = 1, ParentId = parentId, Type = type, Code = code,
            FullCode = $"{parent?.FullCode ?? "WH1"}-{code}"
        });
    }

    private string Code(int id) => _repo.Locations.Single(l => l.Id == id).FullCode;

    // ---------- Места хранения ----------

    [Fact]
    public async Task Перенос_полки_в_другой_стеллаж_меняет_коды_всего_поддерева()
    {
        var result = await _service.UpdateLocationAsync(ShelfA1, new UpdateLocationRequest(RackB, LocationType.Shelf, "01", null));

        Assert.Equal("WH1-B-01", Code(ShelfA1));
        Assert.Equal("WH1-B-01-01", Code(Cell1));
        Assert.Equal("WH1-B-01-02", Code(Cell2));
        Assert.Equal(RackB, _repo.Locations.Single(l => l.Id == ShelfA1).ParentId);
        Assert.Equal(3, result.ChangedLocationIds!.Count);
        Assert.Contains("перепечатайте", result.Message);
    }

    [Fact]
    public async Task Переименование_без_смены_кода_не_требует_наклеек()
    {
        var result = await _service.UpdateLocationAsync(Cell1, new UpdateLocationRequest(ShelfA1, LocationType.Cell, "01", "Подшипники"));

        Assert.Equal("Подшипники", _repo.Locations.Single(l => l.Id == Cell1).Name);
        Assert.Empty(result.ChangedLocationIds!);
    }

    [Fact]
    public async Task Нельзя_вложить_в_себя_во_вложенное_или_вверх_по_уровню()
    {
        await Assert.ThrowsAsync<DomainException>(() =>
            _service.UpdateLocationAsync(RackA, new UpdateLocationRequest(Cell1, LocationType.Rack, "A", null)));   // во вложенное
        await Assert.ThrowsAsync<DomainException>(() =>
            _service.UpdateLocationAsync(RackB, new UpdateLocationRequest(Cell1, LocationType.Rack, "B", null)));   // стеллаж в ячейку
        await Assert.ThrowsAsync<DomainException>(() =>
            _service.UpdateLocationAsync(ShelfA1, new UpdateLocationRequest(RackA, LocationType.Cell, "01", null))); // внутри ячейки — ячейки

        Assert.Equal("WH1-A-01", Code(ShelfA1));
    }

    [Fact]
    public async Task Нельзя_занять_существующий_код_и_вложить_в_место_с_товаром()
    {
        var taken = await Assert.ThrowsAsync<DomainException>(() =>
            _service.UpdateLocationAsync(Cell2, new UpdateLocationRequest(ShelfA1, LocationType.Cell, "01", null)));
        Assert.Contains("WH1-A-01-01", taken.Message);

        // Товар хранится только в конечных местах — в место с товаром вложить нельзя.
        _repo.LocationsWithStock.Add(RackB);
        Add(99, null, LocationType.Shelf, "X");
        var stock = await Assert.ThrowsAsync<DomainException>(() =>
            _service.UpdateLocationAsync(99, new UpdateLocationRequest(RackB, LocationType.Shelf, "X", null)));
        Assert.Contains("товар", stock.Message);
    }

    [Fact]
    public async Task Удалить_можно_только_пустое_неиспользованное_место()
    {
        await Assert.ThrowsAsync<DomainException>(() => _service.DeleteLocationAsync(ShelfA1)); // есть вложенные

        _repo.LocationsWithMovements.Add(Cell1);
        var used = await Assert.ThrowsAsync<DomainException>(() => _service.DeleteLocationAsync(Cell1));
        Assert.Contains("история", used.Message);

        await _service.DeleteLocationAsync(Cell2);
        Assert.DoesNotContain(_repo.Locations, l => l.Id == Cell2);
    }

    // ---------- Склады ----------

    [Fact]
    public async Task Смена_кода_склада_меняет_коды_всех_мест()
    {
        var result = await _service.UpdateWarehouseAsync(1, new UpdateWarehouseRequest("скз", "Склад запчастей", null));

        Assert.Equal("WHZ", _repo.Warehouses[0].Code);
        Assert.Equal("WHZ-A-01-02", Code(Cell2));
        Assert.Equal("WHZ-B", Code(RackB));
        Assert.Equal(5, result.ChangedLocationIds!.Count);

        await Assert.ThrowsAsync<DomainException>(() => _service.UpdateWarehouseAsync(1, new UpdateWarehouseRequest("WH2", "x", null)));
        await Assert.ThrowsAsync<DomainException>(() => _service.DeleteWarehouseAsync(1)); // есть места
        await _service.DeleteWarehouseAsync(2);
    }

    // ---------- Категории, единицы, оборудование ----------

    [Fact]
    public async Task Удаление_категории_оставляет_позиции_без_категории()
    {
        _repo.Categories.Add(new Category { Id = 1, Name = "Подшипники" });
        _repo.Categories.Add(new Category { Id = 2, Name = "Ремни" });
        _repo.ItemCategories.AddRange([1, 1]);

        await Assert.ThrowsAsync<DomainException>(() => _service.UpdateCategoryAsync(2, new UpdateCategoryRequest("подшипники", null)));
        var result = await _service.DeleteCategoryAsync(1);

        Assert.Contains("(2)", result.Message);
        Assert.Single(_repo.Categories);
    }

    [Fact]
    public async Task Единица_целой_только_без_дробных_и_удаляется_только_неиспользованная()
    {
        _repo.Units.Add(new UnitOfMeasure { Id = 1, Code = "м", Name = "метр", IsFractional = true });
        _repo.FractionalUnits.Add(1);
        _repo.ItemUnits.Add(1);

        await Assert.ThrowsAsync<DomainException>(() => _service.UpdateUnitAsync(1, new UpdateUnitRequest("м", "метр", false)));
        await _service.UpdateUnitAsync(1, new UpdateUnitRequest("м.", "метр погонный", true));
        Assert.Equal("м.", _repo.Units[0].Code);

        await Assert.ThrowsAsync<DomainException>(() => _service.DeleteUnitAsync(1));
    }

    [Fact]
    public async Task Оборудование_нельзя_вложить_в_свой_узел()
    {
        _repo.Equipment.Add(new Equipment { Id = 1, Code = "Л1", Name = "Линия" });
        _repo.Equipment.Add(new Equipment { Id = 2, ParentId = 1, Code = "Л1-Н1", Name = "Насос" });

        await Assert.ThrowsAsync<DomainException>(() => _service.UpdateEquipmentAsync(1, new UpdateEquipmentRequest(2, "Л1", "Линия")));
        await Assert.ThrowsAsync<DomainException>(() => _service.DeleteEquipmentAsync(1)); // есть узлы

        _repo.UsedEquipment.Add(2);
        await Assert.ThrowsAsync<DomainException>(() => _service.DeleteEquipmentAsync(2)); // в документах

        await _service.UpdateEquipmentAsync(2, new UpdateEquipmentRequest(null, "Н1", "Насос центробежный"));
        Assert.Null(_repo.Equipment[1].ParentId);
    }
}

/// <summary>Справочники в памяти для проверки правил правки.</summary>
internal class FakeReferenceAdminRepository : IReferenceAdminRepository
{
    public List<Warehouse> Warehouses { get; } = [];
    public List<StorageLocation> Locations { get; } = [];
    public HashSet<int> LocationsWithStock { get; } = [];
    public HashSet<int> LocationsWithMovements { get; } = [];
    public List<Category> Categories { get; } = [];
    public List<int> ItemCategories { get; } = [];  // категории позиций (по одной на позицию)
    public List<UnitOfMeasure> Units { get; } = [];
    public List<int> ItemUnits { get; } = [];
    public HashSet<int> FractionalUnits { get; } = [];
    public List<Equipment> Equipment { get; } = [];
    public HashSet<int> UsedEquipment { get; } = [];

    public Task<Warehouse?> GetWarehouseAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Warehouses.FirstOrDefault(x => x.Id == id));

    public Task<bool> WarehouseCodeExistsAsync(string code, int exceptId, CancellationToken ct = default) =>
        Task.FromResult(Warehouses.Any(x => x.Code == code && x.Id != exceptId));

    public Task<List<StorageLocation>> GetWarehouseLocationsAsync(int warehouseId, CancellationToken ct = default) =>
        Task.FromResult(Locations.Where(x => x.WarehouseId == warehouseId).ToList());

    public Task<StorageLocation?> GetLocationAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Locations.FirstOrDefault(x => x.Id == id));

    public Task<IReadOnlyList<string>> FindFullCodesAsync(IReadOnlyCollection<string> codes, IReadOnlyCollection<int> exceptIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(Locations.Where(x => codes.Contains(x.FullCode) && !exceptIds.Contains(x.Id)).Select(x => x.FullCode).ToList());

    public Task<bool> LocationHasStockAsync(int locationId, CancellationToken ct = default) =>
        Task.FromResult(LocationsWithStock.Contains(locationId));

    public Task<bool> LocationHasMovementsAsync(int locationId, CancellationToken ct = default) =>
        Task.FromResult(LocationsWithMovements.Contains(locationId));

    public Task<Category?> GetCategoryAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Categories.FirstOrDefault(x => x.Id == id));

    public Task<bool> CategoryNameExistsAsync(string name, int exceptId, CancellationToken ct = default) =>
        Task.FromResult(Categories.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) && x.Id != exceptId));

    public Task<int> ClearCategoryAsync(int categoryId, CancellationToken ct = default) =>
        Task.FromResult(ItemCategories.RemoveAll(c => c == categoryId));

    public Task<UnitOfMeasure?> GetUnitAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Units.FirstOrDefault(x => x.Id == id));

    public Task<bool> UnitCodeExistsAsync(string code, int exceptId, CancellationToken ct = default) =>
        Task.FromResult(Units.Any(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase) && x.Id != exceptId));

    public Task<int> CountItemsWithUnitAsync(int unitId, CancellationToken ct = default) =>
        Task.FromResult(ItemUnits.Count(u => u == unitId));

    public Task<bool> UnitHasFractionalQuantitiesAsync(int unitId, CancellationToken ct = default) =>
        Task.FromResult(FractionalUnits.Contains(unitId));

    public Task<Equipment?> GetEquipmentAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Equipment.FirstOrDefault(x => x.Id == id));

    public Task<bool> EquipmentCodeExistsAsync(string code, int exceptId, CancellationToken ct = default) =>
        Task.FromResult(Equipment.Any(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase) && x.Id != exceptId));

    public Task<IReadOnlyList<(int Id, int? ParentId)>> ListEquipmentHierarchyAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<(int Id, int? ParentId)>>(Equipment.Select(e => (e.Id, e.ParentId)).ToList());

    public Task<bool> EquipmentIsUsedAsync(int equipmentId, CancellationToken ct = default) =>
        Task.FromResult(UsedEquipment.Contains(equipmentId));

    public void Remove(object entity)
    {
        switch (entity)
        {
            case Warehouse w: Warehouses.Remove(w); break;
            case StorageLocation l: Locations.Remove(l); break;
            case Category c: Categories.Remove(c); break;
            case UnitOfMeasure u: Units.Remove(u); break;
            case Equipment e: Equipment.Remove(e); break;
        }
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}
