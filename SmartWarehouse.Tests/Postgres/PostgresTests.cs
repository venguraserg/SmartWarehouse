using Microsoft.EntityFrameworkCore;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;
using SmartWarehouse.Infrastructure.Data;
using SmartWarehouse.Infrastructure.Repositories;

namespace SmartWarehouse.Tests.Postgres;

/// <summary>
/// То, что подделки репозиториев проверить не могут: миграции и модель, поиск (ILIKE, экранирование, заметки),
/// инвентаризация с транзакцией — на настоящем PostgreSQL. Каждый тест заводит свой склад и позиции.
/// </summary>
public class PostgresTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);
    private static int _seq;

    private sealed record Rack(int WarehouseId, int RackId, int Cell1, int Cell2, int Floor, int Bearing, int Belt, string Tag);

    /// <summary>Склад со стеллажом C3 (полка 1: ячейки 1, 2; пол) и две позиции; коды уникальны для теста.</summary>
    private async Task<Rack> SeedAsync()
    {
        var tag = $"T{Interlocked.Increment(ref _seq)}{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        await using var db = pg.CreateContext();
        var unit = new UnitOfMeasure { Code = $"шт{tag}", Name = "штука" };
        var warehouse = new Warehouse { Code = tag, Name = $"Склад {tag}" };
        db.AddRange(unit, warehouse);
        await db.SaveChangesAsync();

        StorageLocation Add(StorageLocation? parent, LocationType type, string code)
        {
            var l = new StorageLocation
            {
                Warehouse = warehouse, Parent = parent, Type = type, Code = code,
                FullCode = parent is null ? $"{tag}-{code}" : $"{parent.FullCode}-{code}"
            };
            db.Add(l);
            return l;
        }
        var zone = Add(null, LocationType.Zone, "1");
        var rack = Add(zone, LocationType.Rack, "C3");
        var shelf = Add(rack, LocationType.Shelf, "1");
        var cell1 = Add(shelf, LocationType.Cell, "1");
        var cell2 = Add(shelf, LocationType.Cell, "2");
        var floor = Add(rack, LocationType.Shelf, "0");
        var bearing = new Item { Sku = $"BRG-{tag}", Name = $"Подшипник шариковый 6205-2RS {tag}", Unit = unit };
        var belt = new Item { Sku = $"BLT-{tag}", Name = $"Ремень SPZ 1250 {tag}", Unit = unit };
        db.AddRange(bearing, belt);
        await db.SaveChangesAsync();
        return new Rack(warehouse.Id, rack.Id, cell1.Id, cell2.Id, floor.Id, bearing.Id, belt.Id, tag);
    }

    private StockService Stock(WarehouseDbContext db) => new(new StockRepository(db), new FixedTimeProvider(Now), new ItemNoteRepository(db));

    private InventoryService Inventory(WarehouseDbContext db) =>
        new(new InventoryRepository(db), new LocationRepository(db), new StockRepository(db), Stock(db), new FixedTimeProvider(Now));

    private async Task ReceiveAsync(int item, decimal qty, int location, decimal price = 10)
    {
        await using var db = pg.CreateContext();
        await Stock(db).PostDocumentAsync(new CreateDocumentRequest(MovementType.Receipt, null, null, "Тест", null,
            [new DocumentLineRequest(item, qty, null, location, UnitPrice: price)]));
    }

    private async Task<decimal> BalanceAsync(int location, int item)
    {
        await using var db = pg.CreateContext();
        return await db.Balances.Where(b => b.LocationId == location && b.ItemId == item).SumAsync(b => b.Quantity);
    }

    [PostgresFact]
    public async Task Миграции_применены_и_совпадают_с_моделью()
    {
        await using var db = pg.CreateContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        // Модель в коде и последняя миграция не разошлись (забытая миграция — здесь).
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [PostgresFact]
    public async Task Поиск_номенклатуры_по_словам_заметкам_и_без_подстановок()
    {
        var r = await SeedAsync();
        await using (var db = pg.CreateContext())
        {
            db.ItemNotes.Add(new ItemNote { ItemId = r.Belt, Kind = NoteKind.Usage, Text = "Стоит на гомогенизаторе А1-ОГМ", AuthorName = "Тест", CreatedAt = Now });
            await db.SaveChangesAsync();
        }

        await using var ctx = pg.CreateContext();
        var catalog = new CatalogService(new CatalogRepository(ctx), new FixedTimeProvider(Now), new ItemNoteRepository(ctx));

        Assert.Equal([r.Bearing], (await catalog.ListItemsAsync($"6205 подшипник {r.Tag}", null)).Select(i => i.Id));
        Assert.Empty(await catalog.ListItemsAsync($"6206 подшипник {r.Tag}", null));
        // Слово только в заметке — находится, с отрывком.
        var byNote = Assert.Single(await catalog.ListItemsAsync($"ремень гомогенизатор {r.Tag}", null));
        Assert.Contains("гомогенизаторе", byNote.NoteHit!.Snippet);
        // «%» и «_» — обычные символы, а не «что угодно».
        Assert.Empty(await catalog.ListItemsAsync($"% {r.Tag}", null));
        Assert.Empty(await catalog.ListItemsAsync($"_ {r.Tag}", null));
    }

    [PostgresFact]
    public async Task Поиск_остатков_по_словам_и_заметкам()
    {
        var r = await SeedAsync();
        await ReceiveAsync(r.Bearing, 5, r.Cell1);
        await ReceiveAsync(r.Belt, 2, r.Cell2);
        await using (var db = pg.CreateContext())
        {
            db.ItemNotes.Add(new ItemNote { ItemId = r.Bearing, Kind = NoteKind.Analog, Text = "Подходит FAG 6205-2Z", AuthorName = "Тест", CreatedAt = Now });
            await db.SaveChangesAsync();
        }

        await using var ctx = pg.CreateContext();
        var stock = new StockService(new StockRepository(ctx), new FixedTimeProvider(Now), new ItemNoteRepository(ctx));

        var found = await stock.SearchStockAsync(new StockQuery(WarehouseId: r.WarehouseId, Search: $"{r.Tag} fag"));
        Assert.Equal([r.Bearing], found.Rows.Select(x => x.ItemId));
        Assert.Contains("FAG", found.NoteHits![r.Bearing].Snippet);
        Assert.Single((await stock.SearchStockAsync(new StockQuery(WarehouseId: r.WarehouseId, Search: "spz ремень"))).Rows);
    }

    [PostgresFact]
    public async Task Инвентаризация_проводится_одной_транзакцией()
    {
        var r = await SeedAsync();
        await ReceiveAsync(r.Bearing, 6, r.Cell1, price: 10);
        await ReceiveAsync(r.Belt, 3, r.Cell2, price: 5);
        var actor = new InventoryActor(null, "Кладовщик");

        int id;
        await using (var db = pg.CreateContext())
            id = (await Inventory(db).StartAsync(new StartInventoryRequest(r.RackId), actor)).Header.Id;

        // Каждое действие — как отдельный запрос: свой контекст.
        async Task Do(Func<InventoryService, Task> action)
        {
            await using var db = pg.CreateContext();
            await action(Inventory(db));
        }
        await Do(s => s.CountAsync(id, r.Cell1, new InventoryCountRequest(r.Bearing, 4), actor));
        await Do(s => s.CountAsync(id, r.Cell1, new InventoryCountRequest(r.Belt, 2), actor));   // лишнее — пересорт
        await Do(s => s.CountAsync(id, r.Cell2, new InventoryCountRequest(r.Belt, 1), actor));
        await Do(s => s.CountAsync(id, r.Floor, new InventoryCountRequest(r.Bearing, 1), actor)); // часть недостачи нашлась
        foreach (var place in new[] { r.Cell1, r.Cell2, r.Floor })
            await Do(s => s.CheckAsync(id, place, true, actor));

        InventoryPostResultDto result;
        await using (var db = pg.CreateContext())
            result = await Inventory(db).PostAsync(id, actor);

        Assert.Equal(InventoryStatus.Posted, result.Header.Status);
        Assert.Equal(2, result.Documents.Count); // перемещение (ремни и подшипник) и корректировка «−» (подшипник 1)
        Assert.Equal(4, await BalanceAsync(r.Cell1, r.Bearing));
        Assert.Equal(1, await BalanceAsync(r.Floor, r.Bearing));
        Assert.Equal(2, await BalanceAsync(r.Cell1, r.Belt));
        Assert.Equal(1, await BalanceAsync(r.Cell2, r.Belt));
        await using (var db = pg.CreateContext())
        {
            var checkedAt = await db.Locations.Where(l => l.Id == r.Cell1 || l.Id == r.Cell2 || l.Id == r.Floor).Select(l => l.CheckedAt).ToListAsync();
            Assert.All(checkedAt, c => Assert.Equal(Now, c));
        }
    }

    [PostgresFact]
    public async Task Ошибка_при_проведении_откатывает_всё()
    {
        var r = await SeedAsync();
        await ReceiveAsync(r.Bearing, 6, r.Cell1);
        await ReceiveAsync(r.Belt, 4, r.Cell2);
        var actor = new InventoryActor(null, "Кладовщик");

        int id;
        await using (var db = pg.CreateContext())
        {
            var s = Inventory(db);
            id = (await s.StartAsync(new StartInventoryRequest(r.RackId), actor)).Header.Id;
        }
        await using (var db = pg.CreateContext())
        {
            var s = Inventory(db);
            // Ремни: 2 из 4 в ячейке 2 и 2 лишних в ячейке 1 — перемещение (проводится первым и успевает сохраниться).
            await s.CountAsync(id, r.Cell2, new InventoryCountRequest(r.Belt, 2), actor);
            await s.CountAsync(id, r.Cell1, new InventoryCountRequest(r.Belt, 2), actor);
            await s.CountAsync(id, r.Cell1, new InventoryCountRequest(r.Bearing, 0), actor); // недостача 6
            await s.CheckAsync(id, r.Cell1, true, actor);
            await s.CheckAsync(id, r.Cell2, true, actor);
        }
        // После подсчёта все подшипники переложили на пол — списать 6 из ячейки 1 уже нельзя.
        await using (var db = pg.CreateContext())
            await Stock(db).PostDocumentAsync(new CreateDocumentRequest(MovementType.Transfer, null, null, "Тест", null,
                [new DocumentLineRequest(r.Bearing, 6, r.Cell1, r.Floor)]));
        int documentsBefore;
        await using (var db = pg.CreateContext())
            documentsBefore = await db.Documents.CountAsync();

        await using (var db = pg.CreateContext())
            await Assert.ThrowsAnyAsync<DomainException>(() => Inventory(db).PostAsync(id, actor));

        await using (var db = pg.CreateContext())
        {
            Assert.Equal(documentsBefore, await db.Documents.CountAsync());
            Assert.Equal(InventoryStatus.Open, (await db.Inventories.SingleAsync(i => i.Id == id)).Status);
            Assert.Null((await db.Locations.SingleAsync(l => l.Id == r.Cell1)).CheckedAt);
        }
        Assert.Equal(4, await BalanceAsync(r.Cell2, r.Belt));
        Assert.Equal(6, await BalanceAsync(r.Floor, r.Bearing));
    }
}
