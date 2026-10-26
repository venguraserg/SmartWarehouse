using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Tests;

public class StockServiceTests
{
    private const int ItemId = 1;
    private const int CellA = 10;
    private const int CellB = 20;
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeStockRepository _repo = new();
    private readonly StockService _service;

    public StockServiceTests()
    {
        _repo.Items.Add(new Item { Id = ItemId, Sku = "BRG-6205", Name = "Подшипник 6205", Unit = "шт", MinStock = 10, MaxStock = 30 });
        _repo.Locations.Add(new StorageLocation { Id = CellA, WarehouseId = 1, Code = "A-01" });
        _repo.Locations.Add(new StorageLocation { Id = CellB, WarehouseId = 1, Code = "B-01" });
        _repo.Equipment.Add(new Equipment { Id = 5, Code = "PUMP-1", Name = "Насос 1" });
        _service = new StockService(_repo, new FixedTimeProvider(Now));
    }

    private static MovementRequest Req(MovementType type, decimal qty, int? from = null, int? to = null, int? equipment = null) =>
        new(type, ItemId, qty, from, to, equipment, "Тест", null, null);

    private decimal Balance(int location) =>
        _repo.Balances.FirstOrDefault(b => b.ItemId == ItemId && b.LocationId == location)?.Quantity ?? 0;

    [Fact]
    public async Task Приход_создаёт_остаток_и_запись_журнала()
    {
        var result = await _service.RegisterAsync(Req(MovementType.Receipt, 12, to: CellA));

        Assert.Equal(12, Balance(CellA));
        var movement = Assert.Single(_repo.Movements);
        Assert.Equal(MovementType.Receipt, movement.Type);
        Assert.Equal(Now, movement.OccurredAt);
        Assert.Equal(1, result.Id);
        Assert.Equal(1, _repo.SaveCount);
    }

    [Fact]
    public async Task Повторный_приход_увеличивает_остаток()
    {
        await _service.RegisterAsync(Req(MovementType.Receipt, 5, to: CellA));
        await _service.RegisterAsync(Req(MovementType.Receipt, 2.5m, to: CellA));

        Assert.Equal(7.5m, Balance(CellA));
        Assert.Single(_repo.Balances);
    }

    [Fact]
    public async Task Выдача_уменьшает_остаток_и_привязывается_к_оборудованию()
    {
        await _service.RegisterAsync(Req(MovementType.Receipt, 10, to: CellA));
        await _service.RegisterAsync(Req(MovementType.Issue, 4, from: CellA, equipment: 5));

        Assert.Equal(6, Balance(CellA));
        Assert.Equal(5, _repo.Movements.Last().EquipmentId);
    }

    [Fact]
    public async Task Выдача_больше_остатка_запрещена_и_ничего_не_меняет()
    {
        await _service.RegisterAsync(Req(MovementType.Receipt, 3, to: CellA));

        var ex = await Assert.ThrowsAsync<InsufficientStockException>(
            () => _service.RegisterAsync(Req(MovementType.Issue, 5, from: CellA)));

        Assert.Equal(3, ex.Available);
        Assert.Equal(3, Balance(CellA));
        Assert.Single(_repo.Movements);
    }

    [Fact]
    public async Task Выдача_с_пустого_места_запрещена()
    {
        await Assert.ThrowsAsync<InsufficientStockException>(
            () => _service.RegisterAsync(Req(MovementType.Issue, 1, from: CellA)));
    }

    [Fact]
    public async Task Перемещение_переносит_остаток_между_местами()
    {
        await _service.RegisterAsync(Req(MovementType.Receipt, 10, to: CellA));
        await _service.RegisterAsync(Req(MovementType.Transfer, 4, from: CellA, to: CellB));

        Assert.Equal(6, Balance(CellA));
        Assert.Equal(4, Balance(CellB));
    }

    [Fact]
    public async Task Перемещение_в_то_же_место_запрещено()
    {
        await _service.RegisterAsync(Req(MovementType.Receipt, 10, to: CellA));

        await Assert.ThrowsAsync<DomainException>(
            () => _service.RegisterAsync(Req(MovementType.Transfer, 1, from: CellA, to: CellA)));
    }

    [Fact]
    public async Task Списание_уменьшает_остаток()
    {
        await _service.RegisterAsync(Req(MovementType.Receipt, 10, to: CellA));
        await _service.RegisterAsync(Req(MovementType.WriteOff, 10, from: CellA));

        Assert.Equal(0, Balance(CellA));
    }

    [Fact]
    public async Task Корректировка_работает_в_обе_стороны()
    {
        await _service.RegisterAsync(Req(MovementType.Adjustment, 3, to: CellA));
        await _service.RegisterAsync(Req(MovementType.Adjustment, 1, from: CellA));

        Assert.Equal(2, Balance(CellA));
    }

    [Theory]
    [InlineData(MovementType.Receipt, CellA, null)]          // приход не может быть «откуда»
    [InlineData(MovementType.Receipt, null, null)]
    [InlineData(MovementType.Issue, null, CellA)]            // выдача не может быть «куда»
    [InlineData(MovementType.WriteOff, CellA, CellB)]
    [InlineData(MovementType.Transfer, CellA, null)]
    [InlineData(MovementType.Adjustment, CellA, CellB)]
    [InlineData(MovementType.Adjustment, null, null)]
    public async Task Неверное_направление_операции_отклоняется(MovementType type, int? from, int? to)
    {
        await Assert.ThrowsAsync<DomainException>(() => _service.RegisterAsync(Req(type, 1, from, to)));
        Assert.Empty(_repo.Movements);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Количество_должно_быть_положительным(int qty)
    {
        await Assert.ThrowsAsync<DomainException>(() => _service.RegisterAsync(Req(MovementType.Receipt, qty, to: CellA)));
    }

    [Fact]
    public async Task Несуществующие_позиция_место_и_оборудование_дают_NotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.RegisterAsync(
            new MovementRequest(MovementType.Receipt, 999, 1, null, CellA, null, null, null, null)));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.RegisterAsync(Req(MovementType.Receipt, 1, to: 999)));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.RegisterAsync(Req(MovementType.Receipt, 1, to: CellA, equipment: 999)));
    }

    [Fact]
    public async Task Операции_с_неактивной_позицией_запрещены()
    {
        _repo.Items[0].IsActive = false;

        await Assert.ThrowsAsync<DomainException>(() => _service.RegisterAsync(Req(MovementType.Receipt, 1, to: CellA)));
    }

    [Fact]
    public async Task Ниже_минимума_считается_по_сумме_всех_мест_с_дозаказом_до_максимума()
    {
        await _service.RegisterAsync(Req(MovementType.Receipt, 4, to: CellA));
        await _service.RegisterAsync(Req(MovementType.Receipt, 3, to: CellB));

        var low = Assert.Single(await _service.GetBelowMinimumAsync());
        Assert.Equal(7, low.Total);
        Assert.Equal(23, low.ToOrder); // максимум 30 − остаток 7
    }

    [Fact]
    public async Task Позиция_с_остатком_не_ниже_минимума_не_попадает_в_список()
    {
        await _service.RegisterAsync(Req(MovementType.Receipt, 10, to: CellA));

        Assert.Empty(await _service.GetBelowMinimumAsync());
    }

    [Fact]
    public async Task Без_максимума_дозаказ_до_минимума()
    {
        _repo.Items[0].MaxStock = null;
        await _service.RegisterAsync(Req(MovementType.Receipt, 4, to: CellA));

        var low = Assert.Single(await _service.GetBelowMinimumAsync());
        Assert.Equal(6, low.ToOrder);
    }

    [Fact]
    public async Task Остатки_по_местам_без_нулевых()
    {
        await _service.RegisterAsync(Req(MovementType.Receipt, 5, to: CellA));
        await _service.RegisterAsync(Req(MovementType.Transfer, 5, from: CellA, to: CellB));

        var balance = Assert.Single(await _service.GetBalancesAsync(ItemId));
        Assert.Equal("B-01", balance.LocationCode);
        Assert.Equal(5, balance.Quantity);
    }
}
