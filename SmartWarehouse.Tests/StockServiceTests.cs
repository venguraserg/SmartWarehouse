using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Tests;

public class StockServiceTests
{
    private const int Bearing = 1;   // подшипник, шт (целые)
    private const int Cable = 2;     // кабель, м (дробные)
    private const int CellA = 10;
    private const int CellB = 20;
    private const int Shelf = 30;    // полка с вложенными ячейками — хранить в ней нельзя
    private const int Pump = 5;
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeStockRepository _repo = new();
    private readonly StockService _service;

    public StockServiceTests()
    {
        var pcs = new UnitOfMeasure { Id = 1, Code = "шт", Name = "штука", IsFractional = false };
        var meter = new UnitOfMeasure { Id = 2, Code = "м", Name = "метр", IsFractional = true };

        _repo.Items.Add(new Item { Id = Bearing, Sku = "BRG-6205", Name = "Подшипник 6205", UnitId = 1, Unit = pcs, MinStock = 10, MaxStock = 30 });
        _repo.Items.Add(new Item { Id = Cable, Sku = "CBL-3X1.5", Name = "Кабель ВВГ 3х1,5", UnitId = 2, Unit = meter, MinStock = 0 });

        _repo.Locations.Add(new StorageLocation { Id = Shelf, WarehouseId = 1, Type = LocationType.Shelf, Code = "01", FullCode = "СК1-A-01" });
        _repo.Locations.Add(new StorageLocation { Id = CellA, WarehouseId = 1, ParentId = Shelf, Type = LocationType.Cell, Code = "01", FullCode = "СК1-A-01-01" });
        _repo.Locations.Add(new StorageLocation { Id = CellB, WarehouseId = 1, ParentId = Shelf, Type = LocationType.Cell, Code = "02", FullCode = "СК1-A-01-02" });

        var warehouse = new Warehouse { Id = 1, Code = "СК1", Name = "Склад запчастей" };
        foreach (var location in _repo.Locations)
            location.Warehouse = warehouse;

        _repo.Equipment.Add(new Equipment { Id = Pump, Code = "PUMP-1", Name = "Насос 1" });

        _service = new StockService(_repo, new FixedTimeProvider(Now));
    }

    private static DocumentLineRequest Line(decimal qty, int? from = null, int? to = null, int item = Bearing) =>
        new(item, qty, from, to);

    private static CreateDocumentRequest Doc(MovementType type, params DocumentLineRequest[] lines) =>
        new(type, null, null, "Тест", null, lines);

    private Task<DocumentDto> Post(MovementType type, params DocumentLineRequest[] lines) =>
        _service.PostDocumentAsync(Doc(type, lines));

    private decimal Balance(int location, int item = Bearing) =>
        _repo.Balances.FirstOrDefault(b => b.ItemId == item && b.LocationId == location)?.Quantity ?? 0;

    // ---------- Проведение документов ----------

    [Fact]
    public async Task Приход_создаёт_документ_строки_и_остаток()
    {
        var doc = await _service.PostDocumentAsync(
            new CreateDocumentRequest(MovementType.Receipt, "ТТН 123", null, "Кладовщик", null,
                [Line(12, to: CellA), Line(4.5m, to: CellB, item: Cable)]));

        Assert.Equal("ПР-000001", doc.Number);
        Assert.Equal("ТТН 123", doc.ExternalNumber);
        Assert.Equal(Now, doc.OccurredAt);
        Assert.Equal(2, doc.Lines.Count);
        Assert.Equal("СК1-A-01-01", doc.Lines[0].ToLocation);
        Assert.Equal("м", doc.Lines[1].UnitCode);

        Assert.Equal(12, Balance(CellA));
        Assert.Equal(4.5m, Balance(CellB, Cable));
        Assert.Equal(1, _repo.SaveCount);
    }

    [Fact]
    public async Task Несколько_строк_по_одной_позиции_и_месту_суммируются()
    {
        await Post(MovementType.Receipt, Line(5, to: CellA), Line(3, to: CellA));

        Assert.Equal(8, Balance(CellA));
        Assert.Single(_repo.Balances);
    }

    [Fact]
    public async Task Выдача_уменьшает_остаток_и_привязывается_к_оборудованию()
    {
        await Post(MovementType.Receipt, Line(10, to: CellA));

        var doc = await _service.PostDocumentAsync(
            new CreateDocumentRequest(MovementType.Issue, null, Pump, "Кладовщик", "Ремонт насоса", [Line(4, from: CellA)]));

        Assert.Equal(6, Balance(CellA));
        Assert.Equal(Pump, doc.EquipmentId);
        Assert.Equal("ВЫД-000002", doc.Number);
    }

    [Fact]
    public async Task Если_одна_строка_ошибочна_не_проводится_весь_документ()
    {
        await Post(MovementType.Receipt, Line(3, to: CellA), Line(1, to: CellB));

        // Первая строка корректна, вторая уводит ячейку B в минус.
        var ex = await Assert.ThrowsAsync<InsufficientStockException>(
            () => Post(MovementType.Issue, Line(2, from: CellA), Line(5, from: CellB)));

        Assert.Equal("СК1-A-01-02", ex.LocationCode);
        Assert.Equal(3, Balance(CellA)); // первая строка тоже не применилась
        Assert.Equal(1, Balance(CellB));
        Assert.Single(_repo.Documents);
    }

    [Fact]
    public async Task Две_выдачи_из_одного_места_в_документе_проверяются_вместе()
    {
        await Post(MovementType.Receipt, Line(5, to: CellA));

        await Assert.ThrowsAsync<InsufficientStockException>(
            () => Post(MovementType.Issue, Line(3, from: CellA), Line(3, from: CellA)));

        Assert.Equal(5, Balance(CellA));
    }

    [Fact]
    public async Task Выдача_с_пустого_места_запрещена()
    {
        await Assert.ThrowsAsync<InsufficientStockException>(() => Post(MovementType.Issue, Line(1, from: CellA)));
    }

    [Fact]
    public async Task Перемещение_переносит_остаток_между_местами()
    {
        await Post(MovementType.Receipt, Line(10, to: CellA));
        await Post(MovementType.Transfer, Line(4, from: CellA, to: CellB));

        Assert.Equal(6, Balance(CellA));
        Assert.Equal(4, Balance(CellB));
    }

    [Fact]
    public async Task Списание_и_корректировки_меняют_остаток()
    {
        await Post(MovementType.Adjustment, Line(10, to: CellA));
        await Post(MovementType.Adjustment, Line(1, from: CellA));
        await Post(MovementType.WriteOff, Line(2, from: CellA));

        Assert.Equal(7, Balance(CellA));
    }

    // ---------- Проверки ----------

    [Fact]
    public async Task Пустой_документ_отклоняется()
    {
        await Assert.ThrowsAsync<DomainException>(() => Post(MovementType.Receipt));
    }

    [Theory]
    [InlineData(MovementType.Receipt, CellA, null)]
    [InlineData(MovementType.Receipt, null, null)]
    [InlineData(MovementType.Issue, null, CellA)]
    [InlineData(MovementType.WriteOff, CellA, CellB)]
    [InlineData(MovementType.Transfer, CellA, null)]
    [InlineData(MovementType.Transfer, CellA, CellA)]
    [InlineData(MovementType.Adjustment, CellA, CellB)]
    [InlineData(MovementType.Adjustment, null, null)]
    public async Task Неверное_направление_строки_отклоняется(MovementType type, int? from, int? to)
    {
        await Assert.ThrowsAsync<DomainException>(() => Post(type, Line(1, from, to)));
        Assert.Empty(_repo.Documents);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Количество_должно_быть_положительным(int qty)
    {
        await Assert.ThrowsAsync<DomainException>(() => Post(MovementType.Receipt, Line(qty, to: CellA)));
    }

    [Fact]
    public async Task Дробное_количество_для_штучной_позиции_запрещено()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Post(MovementType.Receipt, Line(1.5m, to: CellA)));
        Assert.Contains("целым", ex.Message);
    }

    [Fact]
    public async Task Хранить_товар_в_месте_с_вложенными_ячейками_нельзя()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Post(MovementType.Receipt, Line(1, to: Shelf)));
        Assert.Contains("СК1-A-01", ex.Message);
    }

    [Fact]
    public async Task Оборудование_можно_указать_только_для_выдачи_и_списания()
    {
        await Assert.ThrowsAsync<DomainException>(() => _service.PostDocumentAsync(
            new CreateDocumentRequest(MovementType.Receipt, null, Pump, null, null, [Line(1, to: CellA)])));
    }

    [Fact]
    public async Task Несуществующие_позиция_место_и_оборудование_дают_NotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Post(MovementType.Receipt, Line(1, to: CellA, item: 999)));
        await Assert.ThrowsAsync<NotFoundException>(() => Post(MovementType.Receipt, Line(1, to: 999)));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.PostDocumentAsync(
            new CreateDocumentRequest(MovementType.Issue, null, 999, null, null, [Line(1, from: CellA)])));
    }

    [Fact]
    public async Task Операции_с_неактивной_позицией_запрещены()
    {
        _repo.Items[0].IsActive = false;

        await Assert.ThrowsAsync<DomainException>(() => Post(MovementType.Receipt, Line(1, to: CellA)));
    }

    // ---------- Остатки и отчёты ----------

    [Fact]
    public async Task Ниже_минимума_считается_по_сумме_всех_мест_с_дозаказом_до_максимума()
    {
        await Post(MovementType.Receipt, Line(4, to: CellA), Line(3, to: CellB));

        var low = Assert.Single(await _service.GetBelowMinimumAsync());
        Assert.Equal(7, low.Total);
        Assert.Equal(23, low.ToOrder); // максимум 30 − остаток 7
        Assert.Equal("шт", low.UnitCode);
    }

    [Fact]
    public async Task Без_максимума_дозаказ_до_минимума()
    {
        _repo.Items[0].MaxStock = null;
        await Post(MovementType.Receipt, Line(4, to: CellA));

        var low = Assert.Single(await _service.GetBelowMinimumAsync());
        Assert.Equal(6, low.ToOrder);
    }

    [Fact]
    public async Task Остатки_по_местам_без_нулевых()
    {
        await Post(MovementType.Receipt, Line(5, to: CellA));
        await Post(MovementType.Transfer, Line(5, from: CellA, to: CellB));

        var balance = Assert.Single(await _service.GetBalancesAsync(Bearing));
        Assert.Equal("СК1-A-01-02", balance.LocationCode);
        Assert.Equal(5, balance.Quantity);
    }

    [Fact]
    public async Task История_движений_показывает_номер_документа()
    {
        await Post(MovementType.Receipt, Line(5, to: CellA));
        await Post(MovementType.Issue, Line(2, from: CellA));

        var history = await _service.GetMovementsAsync(Bearing);
        Assert.Equal(2, history.Count);
        Assert.Equal("ВЫД-000002", history[0].DocumentNumber);
        Assert.Equal("СК1-A-01-01", history[0].FromLocation);
    }

    [Fact]
    public async Task Список_документов_с_фильтром_по_типу()
    {
        await Post(MovementType.Receipt, Line(5, to: CellA), Line(1, to: CellB));
        await Post(MovementType.Issue, Line(2, from: CellA));

        var receipts = await _service.ListDocumentsAsync(MovementType.Receipt);
        var receipt = Assert.Single(receipts);
        Assert.Equal(2, receipt.LineCount);
    }

    // ---------- Просмотр остатков ----------

    private async Task<List<string>> FindAsync(StockQuery query) =>
        (await _service.SearchStockAsync(query)).Rows.Select(r => $"{r.LocationCode}:{r.Sku}").ToList();

    [Fact]
    public async Task Остатки_по_месту_включают_вложенные_места()
    {
        await Post(MovementType.Receipt, Line(5, to: CellA), Line(3, to: CellB, item: Cable));

        Assert.Equal(new[] { "СК1-A-01-01:BRG-6205", "СК1-A-01-02:CBL-3X1.5" }, await FindAsync(new StockQuery(Location: "ск1-a-01")));
        Assert.Equal(new[] { "СК1-A-01-02:CBL-3X1.5" }, await FindAsync(new StockQuery(Location: "СК1-A-01-02")));
    }

    [Fact]
    public async Task Место_можно_искать_без_кода_склада_но_только_целыми_частями()
    {
        await Post(MovementType.Receipt, Line(5, to: CellA), Line(3, to: CellB, item: Cable));

        Assert.Equal(new[] { "СК1-A-01-02:CBL-3X1.5" }, await FindAsync(new StockQuery(Location: "A-01-02")));
        Assert.Empty(await FindAsync(new StockQuery(Location: "СК1-A-0"))); // неполная часть кода не совпадает
    }

    [Fact]
    public async Task Поиск_остатков_по_названию_артикулу_и_складу()
    {
        await Post(MovementType.Receipt, Line(5, to: CellA), Line(3, to: CellB, item: Cable));

        Assert.Equal(new[] { "СК1-A-01-02:CBL-3X1.5" }, await FindAsync(new StockQuery(Search: "кабель")));
        Assert.Equal(new[] { "СК1-A-01-01:BRG-6205" }, await FindAsync(new StockQuery(Search: "brg")));
        Assert.Equal(2, (await FindAsync(new StockQuery(WarehouseId: 1))).Count);
        Assert.Empty(await FindAsync(new StockQuery(WarehouseId: 2)));
    }

    [Fact]
    public async Task Только_ниже_минимума()
    {
        // Подшипник: минимум 10, лежит 5. Кабель: минимум 0.
        await Post(MovementType.Receipt, Line(5, to: CellA), Line(3, to: CellB, item: Cable));

        Assert.Equal(new[] { "СК1-A-01-01:BRG-6205" }, await FindAsync(new StockQuery(BelowMinimum: true)));
    }

    [Fact]
    public async Task Строка_остатка_содержит_место_склад_и_единицу()
    {
        await Post(MovementType.Receipt, Line(4.5m, to: CellB, item: Cable));

        var row = Assert.Single((await _service.SearchStockAsync(new StockQuery())).Rows);
        Assert.Equal("СК1", row.WarehouseCode);
        Assert.Equal("СК1-A-01-02", row.LocationCode);
        Assert.Equal("м", row.UnitCode);
        Assert.Equal(4.5m, row.Quantity);
    }

    [Fact]
    public async Task Пустые_места_не_показываются()
    {
        await Post(MovementType.Receipt, Line(2, to: CellA));
        await Post(MovementType.Issue, Line(2, from: CellA));

        Assert.Empty((await _service.SearchStockAsync(new StockQuery())).Rows);
    }

    // ---------- Сотрудники в документах ----------

    private void AddEmployees()
    {
        _repo.Employees.Add(new Employee { Id = 1, LastName = "Иванов", FirstName = "Иван", MiddleName = "Петрович", Position = "Кладовщик" });
        _repo.Employees.Add(new Employee { Id = 2, LastName = "Петров", FirstName = "Сергей", Position = "Слесарь" });
        _repo.Employees.Add(new Employee { Id = 3, LastName = "Сидоров", FirstName = "Олег", IsActive = false });
    }

    [Fact]
    public async Task Выдача_записывает_кто_провёл_и_кому_выдано()
    {
        AddEmployees();
        await Post(MovementType.Receipt, Line(10, to: CellA));

        var doc = await _service.PostDocumentAsync(new CreateDocumentRequest(
            MovementType.Issue, null, null, "это поле игнорируется", null, [Line(2, from: CellA)],
            PerformedById: 1, ReceivedById: 2));

        Assert.Equal("Иванов И. П.", doc.PerformedBy);
        Assert.Equal(1, doc.PerformedById);
        Assert.Equal("Петров С.", doc.ReceivedBy);
        Assert.Equal(2, doc.ReceivedById);
    }

    [Fact]
    public async Task Получатель_только_для_выдачи()
    {
        AddEmployees();
        await Assert.ThrowsAsync<DomainException>(() => _service.PostDocumentAsync(new CreateDocumentRequest(
            MovementType.Receipt, null, null, null, null, [Line(1, to: CellA)], ReceivedById: 2)));
    }

    [Fact]
    public async Task Неработающий_или_несуществующий_сотрудник_отклоняется()
    {
        AddEmployees();
        await Post(MovementType.Receipt, Line(10, to: CellA));

        await Assert.ThrowsAsync<DomainException>(() => _service.PostDocumentAsync(new CreateDocumentRequest(
            MovementType.Issue, null, null, null, null, [Line(1, from: CellA)], ReceivedById: 3)));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.PostDocumentAsync(new CreateDocumentRequest(
            MovementType.Issue, null, null, null, null, [Line(1, from: CellA)], PerformedById: 99)));
        Assert.Equal(10, Balance(CellA));
    }
}
