using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartWarehouse.Application;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Infrastructure;
using SmartWarehouse.Infrastructure.Data;

// Демо-склад для снимков экранов: стеллажи, позиции, приходы, выдача, заметки, идущая инвентаризация.
// Запуск: dotnet run --project tools/DemoSeed -- "<строка подключения>" <файл для id>
// Вход: demo / demo12345 (администратор и кладовщик), mech / demo12345 (сотрудник).

var connection = args.ElementAtOrDefault(0) ?? throw new ArgumentException("Нужна строка подключения.");
var output = args.ElementAtOrDefault(1) ?? "seed.json";

var services = new ServiceCollection();
services.AddLogging();
services.AddApplication();
services.AddSingleton(new DocumentPrintOptions("Отдел эксплуатации технологического оборудования"));
services.AddInfrastructure(connection);
await using var provider = services.BuildServiceProvider();

async Task<T> In<T>(Func<IServiceProvider, Task<T>> action)
{
    await using var scope = provider.CreateAsyncScope();
    return await action(scope.ServiceProvider);
}

// База и справочники.
await In(async sp =>
{
    var db = sp.GetRequiredService<WarehouseDbContext>();
    await DatabaseSetup.MigrateAsync(db);
    await DbSeeder.SeedAsync(db);
    return 0;
});

var ids = await In(async sp =>
{
    var db = sp.GetRequiredService<WarehouseDbContext>();
    var hasher = sp.GetRequiredService<IPasswordHasher>();
    var now = DateTimeOffset.UtcNow;

    var storekeeper = new Employee { LastName = "Дорофей", FirstName = "Светлана", MiddleName = "Михайловна", Position = "Кладовщик", Department = "ОЭТО" };
    var mechanic = new Employee { LastName = "Иванов", FirstName = "Пётр", MiddleName = "Андреевич", Position = "Слесарь-ремонтник", Department = "ОЭТО" };
    db.AddRange(storekeeper, mechanic);
    db.Users.Add(new UserAccount { Login = "demo", PasswordHash = hasher.Hash("demo12345"), Employee = storekeeper,
        Roles = UserRole.Admin | UserRole.Storekeeper | UserRole.Employee, MustChangePassword = false, CreatedAt = now });
    db.Users.Add(new UserAccount { Login = "mech", PasswordHash = hasher.Hash("demo12345"), Employee = mechanic,
        Roles = UserRole.Employee, MustChangePassword = false, CreatedAt = now });

    var warehouse = new Warehouse { Code = "SK1", Name = "Склад ОЭТО" };
    db.Add(warehouse);
    StorageLocation L(StorageLocation? parent, LocationType type, string code, string? name = null)
    {
        var l = new StorageLocation { Warehouse = warehouse, Parent = parent, Type = type, Code = code, Name = name,
            FullCode = parent is null ? $"SK1-{code}" : $"{parent.FullCode}-{code}" };
        db.Add(l);
        return l;
    }
    var floor1 = L(null, LocationType.Zone, "1");
    var gate = L(null, LocationType.Zone, "V");
    var c3 = L(floor1, LocationType.Rack, "C3", "Подшипники, ремни");
    var c3s2 = L(c3, LocationType.Shelf, "2", "Ремни");
    var c3s1 = L(c3, LocationType.Shelf, "1");
    var cells = Enumerable.Range(1, 4).Select(i => L(c3s1, LocationType.Cell, i.ToString(), i == 1 ? "Подшипники SKF" : null)).ToList();
    var c3floor = L(c3, LocationType.Shelf, "0");
    var a1 = L(floor1, LocationType.Rack, "A1");
    var a1s1 = L(a1, LocationType.Shelf, "1");
    var a1floor = L(a1, LocationType.Shelf, "0");
    var v34 = L(gate, LocationType.Rack, "34");
    var v34floor = L(v34, LocationType.Shelf, "0", "Электродвигатели б/у");

    var pcs = await db.Units.SingleAsync(u => u.Code == "шт");
    var meter = await db.Units.SingleAsync(u => u.Code == "м");
    var bearings = new Category { Name = "Подшипники" };
    var belts = new Category { Name = "Ремни" };
    var seals = new Category { Name = "Уплотнения" };
    var motors = new Category { Name = "Электродвигатели" };
    db.AddRange(bearings, belts, seals, motors);
    Item I(string sku, string name, Category c, UnitOfMeasure u, decimal min = 0)
    {
        var i = new Item { Sku = sku, Name = name, Category = c, Unit = u, MinStock = min };
        db.Add(i);
        return i;
    }
    var b6205 = I("00-00012345", "Подшипник шариковый 6205-2RS SKF", bearings, pcs, 4);
    var b6206 = I("00-00012346", "Подшипник шариковый 6206-2RS SKF", bearings, pcs, 4);
    var b6304 = I("00-00012347", "Подшипник шариковый 6304-2RS SKF", bearings, pcs, 2);
    var spz1250 = I("00-00020011", "Ремень приводной SPZ 1250", belts, pcs, 2);
    var spz1400 = I("00-00020012", "Ремень приводной SPZ 1400", belts, pcs, 2);
    var cuff = I("00-00009402", "Манжета армированная 40×60×10 FKM", seals, pcs, 6);
    var cord = I("00-00009500", "Шнур уплотнительный EPDM 6 мм", seals, meter);
    var motor = I("00-00030001", "Электродвигатель АИР80В4 1,5 кВт", motors, pcs);
    await db.SaveChangesAsync();

    db.ItemNotes.AddRange(
        new ItemNote { ItemId = b6205.Id, Kind = NoteKind.Analog, Text = "Подходит 6205-2Z (FAG, NSK) — проверено на гомогенизаторе А1-ОГМ.", Pinned = true, AuthorName = "Иванов П.А.", CreatedAt = now.AddDays(-12) },
        new ItemNote { ItemId = b6205.Id, Kind = NoteKind.Usage, Text = "Насос центробежный Г2-ОПБ, линия розлива №2 — 2 шт.", AuthorName = "Дорофей С.М.", CreatedAt = now.AddDays(-5) },
        new ItemNote { ItemId = b6205.Id, Kind = NoteKind.Tip, Text = "При посадке греть до 80 °C, молотком не сбивать.", AuthorName = "Иванов П.А.", CreatedAt = now.AddDays(-3) },
        new ItemNote { ItemId = cuff.Id, Kind = NoteKind.Usage, Text = "Плунжеры гомогенизатора, 3 шт на комплект.", AuthorName = "Иванов П.А.", CreatedAt = now.AddDays(-20) });
    await db.SaveChangesAsync();

    return new Dictionary<string, int>
    {
        ["storekeeper"] = storekeeper.Id, ["mechanic"] = mechanic.Id, ["rackC3"] = c3.Id, ["rackA1"] = a1.Id,
        ["cell1"] = cells[0].Id, ["cell2"] = cells[1].Id, ["cell3"] = cells[2].Id, ["cell4"] = cells[3].Id,
        ["c3shelf2"] = c3s2.Id, ["c3floor"] = c3floor.Id, ["a1shelf1"] = a1s1.Id, ["a1floor"] = a1floor.Id, ["v34floor"] = v34floor.Id,
        ["b6205"] = b6205.Id, ["b6206"] = b6206.Id, ["b6304"] = b6304.Id, ["spz1250"] = spz1250.Id, ["spz1400"] = spz1400.Id,
        ["cuff"] = cuff.Id, ["cord"] = cord.Id, ["motor"] = motor.Id
    };
});

// Приходы, выдача — через те же сервисы, что и в программе.
await In(async sp =>
{
    var stock = sp.GetRequiredService<IStockService>();
    DocumentLineRequest Line(string item, decimal qty, string place, decimal price) => new(ids[item], qty, null, ids[place], UnitPrice: price);
    await stock.PostDocumentAsync(new CreateDocumentRequest(MovementType.Receipt, "0000016", null, "Дорофей С.М.", "Накладная 1С",
    [
        Line("b6205", 14, "cell1", 16.40m), Line("b6206", 6, "cell1", 18.20m), Line("b6304", 4, "cell2", 21.00m),
        Line("spz1250", 6, "c3shelf2", 15.90m), Line("spz1400", 3, "c3shelf2", 17.30m), Line("cuff", 9, "cell3", 4.10m),
        Line("cord", 25.5m, "a1shelf1", 1.25m), Line("motor", 2, "v34floor", 310.00m)
    ], PerformedById: ids["storekeeper"]));
    await stock.PostDocumentAsync(new CreateDocumentRequest(MovementType.Issue, null, null, "Дорофей С.М.", "Ремонт насоса Г2-ОПБ",
        [new DocumentLineRequest(ids["b6205"], 2, ids["cell1"], null)], PerformedById: ids["storekeeper"], ReceivedById: ids["mechanic"]));
    return 0;
});

// Идущая инвентаризация стеллажа C3: часть мест посчитана, есть расхождение.
var inventoryId = await In(async sp =>
{
    var inventory = sp.GetRequiredService<IInventoryService>();
    var actor = new InventoryActor(ids["storekeeper"], "Дорофей С.М.");
    var inv = await inventory.StartAsync(new StartInventoryRequest(ids["rackC3"]), actor);
    await inventory.CountAsync(inv.Header.Id, ids["c3shelf2"], new InventoryCountRequest(ids["spz1250"], 6), actor);
    await inventory.CountAsync(inv.Header.Id, ids["c3shelf2"], new InventoryCountRequest(ids["spz1400"], 3), actor);
    await inventory.CheckAsync(inv.Header.Id, ids["c3shelf2"], true, actor);
    await inventory.CountAsync(inv.Header.Id, ids["cell1"], new InventoryCountRequest(ids["b6205"], 11), actor);
    await inventory.CountAsync(inv.Header.Id, ids["cell1"], new InventoryCountRequest(ids["b6206"], 6), actor);
    await inventory.CheckAsync(inv.Header.Id, ids["cell1"], true, actor);
    return inv.Header.Id;
});
ids["inventory"] = inventoryId;

// Открытый запрос сотрудника — для главной под ролью «Сотрудник».
await In(async sp =>
{
    var requests = sp.GetRequiredService<IIssueRequestService>();
    var actor = new IssueRequestActor(ids["mechanic"], "Иванов П.А.", false, null);
    await requests.CreateAsync(new CreateIssueRequestRequest(null, "Для ремонта насоса",
        [new IssueRequestLineRequest(ids["b6304"], 2), new IssueRequestLineRequest(ids["cuff"], 4)]), actor);
    return 0;
});

await File.WriteAllTextAsync(output, JsonSerializer.Serialize(ids));
Console.WriteLine($"Демо-данные готовы: {output}");
