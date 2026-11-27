using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Tests;

public class InventoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);

    // 1-й этаж › стеллаж C3: полка 1 (ячейки 1, 2), полка 0 (пол).
    private const int Zone = 1, Rack = 2, Shelf1 = 3, Cell1 = 4, Cell2 = 5, Floor = 6;
    private const int Bearing = 1, Belt = 2, Motor = 3;

    private static readonly InventoryActor Storekeeper = new(null, "Дорофей С.М.");

    private readonly FakeStockRepository _stock = new();
    private readonly FakeLocationRepository _locations = new();
    private readonly FakeInventoryRepository _repo;
    private readonly StockService _stockService;
    private readonly InventoryService _service;

    public InventoryTests()
    {
        var warehouse = new Warehouse { Id = 1, Code = "SK1", Name = "Склад" };
        _locations.Warehouses.Add(warehouse);
        void Add(int id, int? parent, LocationType type, string code, string full)
        {
            var l = new StorageLocation { Id = id, WarehouseId = 1, Warehouse = warehouse, ParentId = parent, Type = type, Code = code, FullCode = full };
            _stock.Locations.Add(l);
            _locations.Locations.Add(l);
        }
        Add(Zone, null, LocationType.Zone, "1", "SK1-1");
        Add(Rack, Zone, LocationType.Rack, "C3", "SK1-1-C3");
        Add(Shelf1, Rack, LocationType.Shelf, "1", "SK1-1-C3-1");
        Add(Cell1, Shelf1, LocationType.Cell, "1", "SK1-1-C3-1-1");
        Add(Cell2, Shelf1, LocationType.Cell, "2", "SK1-1-C3-1-2");
        Add(Floor, Rack, LocationType.Shelf, "0", "SK1-1-C3-0");

        var pcs = new UnitOfMeasure { Id = 1, Code = "шт", Name = "штука" };
        _stock.Items.Add(new Item { Id = Bearing, Sku = "BRG-6205", Name = "Подшипник 6205", UnitId = 1, Unit = pcs, IsActive = true });
        _stock.Items.Add(new Item { Id = Belt, Sku = "BLT-1250", Name = "Ремень SPZ 1250", UnitId = 1, Unit = pcs, IsActive = true });
        _stock.Items.Add(new Item { Id = Motor, Sku = "MTR-80", Name = "Электродвигатель АИР80", UnitId = 1, Unit = pcs, IsActive = true });

        var clock = new FixedTimeProvider(Now);
        _stockService = new StockService(_stock, clock, new FakeItemNoteRepository());
        _repo = new FakeInventoryRepository(_stock);
        _service = new InventoryService(_repo, _locations, _stockService, clock);
    }

    private Task ReceiveAsync(int item, decimal qty, int location, decimal price = 10) =>
        _stockService.PostDocumentAsync(new CreateDocumentRequest(MovementType.Receipt, null, null, "Тест", null,
            [new DocumentLineRequest(item, qty, null, location, UnitPrice: price)]));

    private decimal Balance(int location, int item) =>
        _stock.Balances.Where(b => b.LocationId == location && b.ItemId == item).Sum(b => b.Quantity);

    [Fact]
    public async Task Места_стеллажа_сверху_вниз_и_одна_инвентаризация_на_место()
    {
        await ReceiveAsync(Bearing, 6, Cell1);
        var inv = await _service.StartAsync(new StartInventoryRequest(Rack), Storekeeper);

        Assert.Equal("1-й этаж · Стеллаж C3", inv.Header.RootTitle);
        Assert.Equal([Cell1, Cell2, Floor], inv.Places.Select(p => p.LocationId));
        Assert.Equal(["C3 · п.1 · яч.1", "C3 · п.1 · яч.2", "C3 · пол"], inv.Places.Select(p => p.Title));
        Assert.Equal(["Полка 1", "Полка 1", "Под стеллажом"], inv.Places.Select(p => p.Shelf));
        Assert.Equal(1, inv.Places[0].BookPositions);

        await Assert.ThrowsAsync<DomainException>(() => _service.StartAsync(new StartInventoryRequest(Rack), Storekeeper));
        await Assert.ThrowsAsync<DomainException>(() => _service.StartAsync(new StartInventoryRequest(Cell1), Storekeeper));
        await Assert.ThrowsAsync<DomainException>(() => _service.StartAsync(new StartInventoryRequest(Zone), Storekeeper));
    }

    [Fact]
    public async Task Подсчёт_пересорт_недостача_и_проведение()
    {
        await ReceiveAsync(Bearing, 6, Cell1, price: 10);
        await ReceiveAsync(Belt, 3, Cell2, price: 5);
        var id = (await _service.StartAsync(new StartInventoryRequest(Rack), Storekeeper)).Header.Id;

        // Ячейка 1: подшипников 4 из 6; нашлись 2 ремня, которые числятся в ячейке 2.
        await _service.CountAsync(id, Cell1, new InventoryCountRequest(Bearing, 4), Storekeeper);
        var cell1 = await _service.CountAsync(id, Cell1, new InventoryCountRequest(Belt, 2), Storekeeper);
        var extra = cell1.Items.Single(i => i.ItemId == Belt);
        Assert.Equal(0, extra.Book);
        Assert.Contains("C3 · п.1 · яч.2", extra.Elsewhere);
        Assert.Equal(Cell2, cell1.NextLocationId);

        // Ячейку 2 нельзя отметить, пока ремень не посчитан.
        await Assert.ThrowsAsync<DomainException>(() => _service.CheckAsync(id, Cell2, true, Storekeeper));
        await _service.CountAsync(id, Cell2, new InventoryCountRequest(Belt, 1), Storekeeper);
        await _service.CheckAsync(id, Cell1, true, Storekeeper);
        await _service.CheckAsync(id, Cell2, true, Storekeeper);
        await _service.CheckAsync(id, Floor, true, Storekeeper);

        var sheet = await _service.GetSheetAsync(id);
        var bearing = sheet.Rows.Single(r => r.ItemId == Bearing);
        Assert.Equal(InventoryAction.Shortage, bearing.Action);
        Assert.Equal(-2, bearing.Delta);
        Assert.Equal(20, bearing.Value);
        var beltHere = sheet.Rows.Single(r => r.ItemId == Belt && r.LocationId == Cell1);
        Assert.Equal(2, beltHere.Moved);
        Assert.Equal(InventoryAction.None, beltHere.Action);
        Assert.Contains("← C3 · п.1 · яч.2", beltHere.MoveText);
        Assert.Equal(1, sheet.ShortagePositions);
        Assert.Equal(1, sheet.MovePositions);
        Assert.Empty(sheet.UncheckedPlaces);

        var result = await _service.PostAsync(id, Storekeeper);

        Assert.Equal(InventoryStatus.Posted, result.Header.Status);
        Assert.Equal(2, result.Documents.Count); // перемещение и корректировка «−»
        Assert.Equal(4, Balance(Cell1, Bearing));
        Assert.Equal(2, Balance(Cell1, Belt));
        Assert.Equal(1, Balance(Cell2, Belt));
        Assert.All(_stock.Locations.Where(l => l.Id is Cell1 or Cell2 or Floor), l => Assert.Equal(Now, l.CheckedAt));
        Assert.Null(_stock.Locations.Single(l => l.Id == Rack).CheckedAt);

        await Assert.ThrowsAsync<DomainException>(() => _service.CountAsync(id, Cell1, new InventoryCountRequest(Bearing, 5), Storekeeper));
    }

    [Fact]
    public async Task Излишек_оприходуется_вне_учёта_а_непроверенные_места_не_трогаются()
    {
        await ReceiveAsync(Bearing, 6, Cell1);
        var id = (await _service.StartAsync(new StartInventoryRequest(Rack), Storekeeper)).Header.Id;

        await _service.CountAsync(id, Floor, new InventoryCountRequest(Motor, 1), Storekeeper);
        await _service.CheckAsync(id, Floor, true, Storekeeper);
        // Ячейка 1 посчитана с недостачей, но не отмечена — при проведении не трогается.
        await _service.CountAsync(id, Cell1, new InventoryCountRequest(Bearing, 1), Storekeeper);

        var sheet = await _service.GetSheetAsync(id);
        Assert.Equal(InventoryAction.Surplus, sheet.Rows.Single(r => r.ItemId == Motor).Action);
        Assert.Equal(InventoryAction.None, sheet.Rows.Single(r => r.ItemId == Bearing).Action);
        Assert.Contains("C3 · п.1 · яч.1", sheet.UncheckedPlaces);

        var result = await _service.PostAsync(id, Storekeeper);

        Assert.Single(result.Documents);
        Assert.Equal(1, Balance(Floor, Motor));
        var batch = _stock.Batches.Single(b => b.ItemId == Motor);
        Assert.True(batch.OffBook);
        Assert.Equal(6, Balance(Cell1, Bearing));
    }

    [Fact]
    public async Task Подсчёт_отменяется_дробное_и_чужое_место_не_принимаются()
    {
        await ReceiveAsync(Bearing, 6, Cell1);
        var id = (await _service.StartAsync(new StartInventoryRequest(Shelf1), Storekeeper)).Header.Id;

        await Assert.ThrowsAsync<DomainException>(() => _service.CountAsync(id, Cell1, new InventoryCountRequest(Bearing, 1.5m), Storekeeper));
        await Assert.ThrowsAsync<DomainException>(() => _service.CountAsync(id, Floor, new InventoryCountRequest(Bearing, 1), Storekeeper)); // не на этой полке
        await Assert.ThrowsAsync<DomainException>(() => _service.CountAsync(id, Cell1, new InventoryCountRequest(Bearing, -1), Storekeeper));

        await _service.CountAsync(id, Cell1, new InventoryCountRequest(Bearing, 6), Storekeeper);
        var place = await _service.CountAsync(id, Cell1, new InventoryCountRequest(Bearing, null), Storekeeper);
        Assert.Null(place.Items.Single().Counted);

        await Assert.ThrowsAsync<DomainException>(() => _service.PostAsync(id, Storekeeper)); // нет проверенных мест
        var cancelled = await _service.CancelAsync(id, Storekeeper);
        Assert.Equal(InventoryStatus.Cancelled, cancelled.Status);
        // После отмены можно начать заново.
        await _service.StartAsync(new StartInventoryRequest(Rack), Storekeeper);
    }

    [Fact]
    public async Task В_документ_идёт_разница_на_момент_подсчёта()
    {
        await ReceiveAsync(Bearing, 6, Cell1);
        var id = (await _service.StartAsync(new StartInventoryRequest(Rack), Storekeeper)).Header.Id;
        await _service.CountAsync(id, Cell1, new InventoryCountRequest(Bearing, 5), Storekeeper);
        await _service.CheckAsync(id, Cell1, true, Storekeeper);

        // После подсчёта 2 шт переложили на пол обычным перемещением.
        await _stockService.PostDocumentAsync(new CreateDocumentRequest(MovementType.Transfer, null, null, "Тест", null,
            [new DocumentLineRequest(Bearing, 2, Cell1, Floor)]));

        await _service.PostAsync(id, Storekeeper);

        // Недостача 1 (6 → 5) списана с того, что осталось: 4 − 1 = 3.
        Assert.Equal(3, Balance(Cell1, Bearing));
        Assert.Equal(2, Balance(Floor, Bearing));
    }
}

internal class FakeInventoryRepository(FakeStockRepository stock) : IInventoryRepository
{
    public List<InventoryCount> Inventories { get; } = [];

    private InventoryCount Attach(InventoryCount inv)
    {
        inv.RootLocation = stock.Locations.First(l => l.Id == inv.RootLocationId);
        foreach (var line in inv.Lines)
            line.Item = stock.Items.First(i => i.Id == line.ItemId);
        return inv;
    }

    public Task<InventoryCount?> GetAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Inventories.FirstOrDefault(x => x.Id == id) is { } inv ? Attach(inv) : null);

    public Task<IReadOnlyList<InventoryCount>> ListAsync(int take, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<InventoryCount>>(Inventories.OrderByDescending(x => x.Id).Take(take).Select(Attach).ToList());

    public Task<IReadOnlyList<InventoryCount>> ListOpenAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<InventoryCount>>(Inventories.Where(x => x.Status == InventoryStatus.Open).Select(Attach).ToList());

    public Task<IReadOnlyList<StockBalance>> ListStockAsync(IReadOnlyCollection<int> locationIds, CancellationToken ct = default)
    {
        var rows = stock.Balances.Where(b => locationIds.Contains(b.LocationId) && b.Quantity > 0).ToList();
        foreach (var b in rows)
        {
            b.Item = stock.Items.First(i => i.Id == b.ItemId);
            b.Batch = stock.Batches.First(x => x.Id == b.BatchId);
        }
        return Task.FromResult<IReadOnlyList<StockBalance>>(rows);
    }

    public Task<Item?> GetItemAsync(int itemId, CancellationToken ct = default) =>
        Task.FromResult(stock.Items.FirstOrDefault(i => i.Id == itemId));

    public Task<IReadOnlyList<(int ItemId, int LocationId, decimal Quantity)>> ListItemPlacesAsync(
        IReadOnlyCollection<int> itemIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<(int, int, decimal)>>(stock.Balances
            .Where(b => itemIds.Contains(b.ItemId) && b.Quantity > 0)
            .GroupBy(b => (b.ItemId, b.LocationId))
            .Select(g => (g.Key.ItemId, g.Key.LocationId, g.Sum(b => b.Quantity)))
            .ToList());

    public Task SetCheckedAsync(IReadOnlyCollection<int> locationIds, DateTimeOffset at, CancellationToken ct = default)
    {
        foreach (var l in stock.Locations.Where(l => locationIds.Contains(l.Id)))
            l.CheckedAt = at;
        return Task.CompletedTask;
    }

    public void Add(InventoryCount inventory)
    {
        inventory.Id = Inventories.Select(x => x.Id).DefaultIfEmpty(0).Max() + 1;
        Inventories.Add(inventory);
    }

    public void Remove(InventoryLine line) { }

    public void Remove(InventoryPlace place) { }

    public Task SaveChangesAsync(CancellationToken ct = default) => stock.SaveChangesAsync(ct);

    public Task InTransactionAsync(Func<Task> action, CancellationToken ct = default) => action();
}
