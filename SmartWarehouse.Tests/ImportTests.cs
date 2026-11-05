using ClosedXML.Excel;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Infrastructure.Import;

namespace SmartWarehouse.Tests;

public class ImportTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);

    // ---------- Адреса и места ----------

    [Theory]
    [InlineData("2A.2.1.3", new[] { "2A", "2", "1", "3" })]
    [InlineData(" c5.2 ", new[] { "C5", "2" })]
    [InlineData("10.0", new[] { "10", "0" })]
    public void Адрес_разбирается(string address, string[] expected) =>
        Assert.Equal(expected, OpeningImportService.ParseAddress(address));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("шкаф свар")]
    [InlineData("C5.2.к")]
    [InlineData("2A..1")]
    public void Адрес_не_по_схеме(string? address) =>
        Assert.Null(OpeningImportService.ParseAddress(address));

    [Fact]
    public void План_мест_товар_на_полке_с_ячейками_и_типы()
    {
        var plan = OpeningImportService.PlanLocations([["2B", "3"], ["2B", "3", "1"], ["C5", "2"], ["2A", "1", "1", "3"]]);

        // «2B.3» имеет вложенное «2B.3.1» — товар «2B.3» кладётся в «2B.3.0».
        Assert.Equal(["2B", "3", "0"], plan.Targets["2B.3"]);
        Assert.Equal(["2B", "3", "1"], plan.Targets["2B.3.1"]);
        Assert.Contains("2B.3.0", plan.Loose);

        Assert.Equal(LocationType.Cell, plan.Types["2B.3.1"]);
        Assert.Equal(LocationType.Shelf, plan.Types["2B.3"]);
        Assert.Equal(LocationType.Rack, plan.Types["2B"]);
        Assert.Equal(LocationType.Cell, plan.Types["C5.2"]);
        Assert.Equal(LocationType.Shelf, plan.Types["C5"]);
        Assert.Equal(LocationType.Zone, plan.Types["2A"]);
        Assert.Equal(LocationType.Cell, plan.Types["2A.1.1.3"]);
    }

    // ---------- МОЛ ↔ сотрудник ----------

    [Fact]
    public void МОЛ_сопоставляется_по_фамилии_и_инициалам()
    {
        Assert.Equal(("Рудаков", "М.", "А."), PersonNames.Parse("Рудаков М.А.")!.Value);
        Assert.Equal(("Дорофей", "С.", "М."), PersonNames.Parse(" Дорофей С. М. ")!.Value);
        Assert.Null(PersonNames.Parse("Отдел эксплуатации"));

        EmployeeDto Emp(int id, string last, string first, string? middle) =>
            new(id, null, last, first, middle, null, null, null, true, $"{last} {first} {middle}", last, null, null);
        var staff = new[] { Emp(1, "Дорофей", "Сергей", "Михайлович"), Emp(2, "Дорофей", "Дмитрий", "Николаевич") };

        Assert.Equal(1, PersonNames.Find(staff, "Дорофей С.М.")?.Id);
        Assert.Equal(2, PersonNames.Find(staff, "Дорофей Д.Н.")?.Id);
        Assert.Null(PersonNames.Find(staff, "Дорофей А.А."));
    }

    // ---------- Чтение Excel ----------

    [Fact]
    public void Сальдовая_ведомость_1С_читается_по_уровням()
    {
        using var book = new XLWorkbook();
        var ws = book.AddWorksheet("TDSheet");
        ws.Cell(1, 1).Value = "Открытое акционерное общество";
        ws.Cell(3, 1).Value = "Остатки на: 31.10.2026";
        void Row(int r, int level, params XLCellValue[] values)
        {
            for (var i = 0; i < values.Length; i++)
                ws.Cell(r, i + 1).Value = values[i];
            if (level > 0)
                ws.Row(r).OutlineLevel = level;
        }
        Row(10, 0, "10.6", 820.5);
        Row(11, 1, "Отдел эксплуатации технологического оборудования", 820.5);
        Row(12, 2, "Дорофей С.М.", 820.5);
        Row(13, 3, 23147, "Датчик индуктивный", "шт", "х", 720);
        Row(14, 4, 23147, "ТТН 1056230 от 01.11.2023 г.", "шт", 60, 12, 720);
        Row(15, 3, 23276, "Кабель", "м", "х", 100.5);
        Row(16, 4, 23276, "б/н", "м", 50, 100.5); // цена не напечатана
        Row(17, 0, "Итого:", 820.5);

        var sheet = Read(book, s => new ExcelImportReader().ReadSaldo(s, "ведомость.xlsx"));

        Assert.Equal("31.10.2026", sheet.AsOf);
        Assert.Equal(2, sheet.Batches.Count);
        var b = sheet.Batches[0];
        Assert.Equal(("10.6", "Дорофей С.М.", "23147", "Датчик индуктивный", "шт"), (b.Account, b.Mol, b.Code, b.Name, b.Unit));
        Assert.Equal((60m, 12m, 720m), (b.Price, b.Quantity, b.Value));
        Assert.Equal("ТТН 1056230 от 01.11.2023 г.", b.Document);
        Assert.Equal((null, 50m, 100.5m), (sheet.Batches[1].Price, sheet.Batches[1].Quantity, sheet.Batches[1].Value));
    }

    [Fact]
    public void Файл_начальных_остатков_читается_по_заголовкам()
    {
        using var book = new XLWorkbook();
        var ws = book.AddWorksheet("Импорт");
        string[] head = ["Код 1С", "Наименование (1С)", "Ед. изм.", "Количество", "Стоимость, руб.", "Документ поступления",
                         "Дата документа", "Где", "МОЛ в 1С", "Адрес хранения"];
        for (var i = 0; i < head.Length; i++)
            ws.Cell(1, i + 1).Value = head[i];
        XLCellValue[] r1 = ["60603", "Редуктор", "шт", 1, 2065.12, "УПД 368", new DateTime(2023, 11, 30), "Склад", "Дорофей С.М.", "C3.1.4"];
        XLCellValue[] r2 = ["76242", "Кондиционер", "шт", 7, 13916, "ТТН 1145525", "10.09.2026", "Подотчёт: Рудаков М.А.", "Рудаков М.А.", ""];
        for (var i = 0; i < r1.Length; i++)
        {
            ws.Cell(2, i + 1).Value = r1[i];
            ws.Cell(3, i + 1).Value = r2[i];
        }

        var file = Read(book, s => new ExcelImportReader().ReadOpening(s));

        Assert.Empty(file.Issues);
        Assert.Equal(2, file.Rows.Count);
        Assert.Equal(("60603", 1m, 2065.12m, "C3.1.4", false), (file.Rows[0].Code, file.Rows[0].Quantity, file.Rows[0].Value, file.Rows[0].Address, file.Rows[0].Custody));
        Assert.Equal(new DateOnly(2023, 11, 30), file.Rows[0].DocumentDate);
        Assert.True(file.Rows[1].Custody);
        Assert.Equal(new DateOnly(2026, 9, 10), file.Rows[1].DocumentDate);
    }

    private static T Read<T>(XLWorkbook book, Func<Stream, T> read)
    {
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        stream.Position = 0;
        return read(stream);
    }

    // ---------- Проверка импорта (без загрузки) ----------

    [Fact]
    public async Task Проверка_импорта_считает_что_будет_создано_и_пропускает_плохие_строки()
    {
        var catalogRepo = new FakeCatalogRepository();
        catalogRepo.Units.Add(new UnitOfMeasure { Id = 1, Code = "шт", Name = "штука" });
        var locationRepo = new FakeLocationRepository();
        locationRepo.Warehouses.Add(new Warehouse { Id = 1, Code = "WH1", Name = "Склад" });
        var employeeRepo = new FakeEmployeeRepository();
        var service = new OpeningImportService(
            new CatalogService(catalogRepo, new FixedTimeProvider(Now)), new LocationService(locationRepo),
            new EmployeeService(employeeRepo), new StockService(new FakeStockRepository(), new FixedTimeProvider(Now)));

        OpeningRow Row(int line, string code, decimal qty, decimal value, string? address, bool custody = false, string unit = "шт", string? mol = null) =>
            new(line, "10.6", code, "Позиция " + code, unit, qty, value, "ТТН 1", new DateOnly(2025, 1, 10), custody, mol, address, null, null);

        var file = new OpeningFile(
        [
            Row(2, "60603", 2, 100, "C3.1.4"),
            Row(3, "60604", 1, 10, "C3.1"),             // у C3.1 есть ячейка C3.1.4 — товар в C3.1.0
            Row(4, "60605", 1.5m, 15, "C3.1.4"),        // дробное в штуках
            Row(5, "60606", 3, 30, "шкаф свар"),         // адрес не по схеме
            Row(6, "76242", 7, 13916, null, custody: true, mol: "Рудаков М.А."),
            Row(7, "60607", 10, 5, "C3.2", unit: "м"),   // новая единица (дробная)
        ], []);

        var result = await service.ImportAsync(file, 1, apply: false, new ImportActor(null, "Админ"));

        Assert.False(result.Applied);
        Assert.Equal(4, result.RowsImported);
        Assert.Equal((3, 1), (result.WarehouseBatches, result.CustodyBatches));
        Assert.Equal(4, result.ItemsNew);
        Assert.Equal(1, result.UnitsNew);
        Assert.Equal(1, result.EmployeesNew);
        // C3, C3.1, C3.1.4, C3.1.0, C3.2 + временное IMP
        Assert.Equal(6, result.LocationsNew);
        Assert.Equal([4, 5], result.Issues.Where(i => i.Skipped).Select(i => i.Line));
        Assert.Contains(result.Notes, n => n.Contains("C3.1.0"));
        Assert.Empty(catalogRepo.Items); // проверка ничего не создаёт
    }

    // ---------- Сверка ----------

    [Fact]
    public async Task Сверка_со_складом_и_подотчётом()
    {
        var stock = new FakeStockRepository();
        var unit = new UnitOfMeasure { Id = 1, Code = "шт", Name = "штука" };
        Item ItemOf(int id, string sku) => new() { Id = id, Sku = sku, Name = "Позиция " + sku, UnitId = 1, Unit = unit };
        stock.Items.AddRange([ItemOf(1, "23147"), ItemOf(2, "23148"), ItemOf(3, "99999"), ItemOf(4, "76242")]);
        stock.Batches.AddRange([new Batch { Id = 1, ItemId = 1, UnitPrice = 60 }, new Batch { Id = 2, ItemId = 2, UnitPrice = 10 },
                                new Batch { Id = 3, ItemId = 3, UnitPrice = 5 }, new Batch { Id = 4, ItemId = 4, UnitPrice = 1988 }]);
        stock.Balances.AddRange([
            new StockBalance { ItemId = 1, LocationId = 1, BatchId = 1, Quantity = 12 },  // совпадает
            new StockBalance { ItemId = 2, LocationId = 1, BatchId = 2, Quantity = 3 },   // в 1С — 5
            new StockBalance { ItemId = 3, LocationId = 1, BatchId = 3, Quantity = 1 }]); // в 1С нет
        stock.EmployeeBalances.Add(new EmployeeBalance { EmployeeId = 7, ItemId = 4, BatchId = 4, Quantity = 7 });

        var employees = new FakeEmployeeRepository();
        employees.Employees.Add(new Employee { Id = 7, LastName = "Рудаков", FirstName = "Максим", MiddleName = "Андреевич", IsActive = true });
        var service = new ReconcileService(stock, new EmployeeService(employees));

        SaldoBatch B(string mol, string code, decimal qty, decimal value) => new("10.6", "Отдел", mol, code, "Позиция " + code, "шт", "ТТН", null, qty, value);
        var sheets = new[]
        {
            new SaldoSheet("склад.xlsx", "31.10.2026", [B("Дорофей С.М.", "23147", 12, 720), B("Дорофей С.М.", "23148", 5, 50), B("Дорофей С.М.", "55555", 2, 20)]),
            new SaldoSheet("рудаков.xlsx", "31.10.2026", [B("Рудаков М.А.", "76242", 7, 13916), B("Иванов И.Г.", "60081", 1, 1.67m)])
        };

        var byValue = await service.ReconcileAsync(sheets, null);
        Assert.Equal("Рудаков М.А.", byValue.WarehouseMol); // не указан — МОЛ с наибольшей стоимостью

        var result = await service.ReconcileAsync(sheets, "Дорофей С.М.");
        Assert.Equal("Дорофей С.М.", result.WarehouseMol);
        ReconcileStatus StatusOf(string code) => result.Rows.Single(r => r.Code == code).Status;
        Assert.Equal(ReconcileStatus.Match, StatusOf("76242"));
        Assert.Equal(ReconcileStatus.OnlyIn1C, StatusOf("60081")); // МОЛ не найден
        Assert.Contains(result.Notes, n => n.Contains("Иванов И.Г."));
    }

    [Fact]
    public async Task Сверка_склада_по_указанному_МОЛ()
    {
        var stock = new FakeStockRepository();
        var unit = new UnitOfMeasure { Id = 1, Code = "шт", Name = "штука" };
        Item ItemOf(int id, string sku) => new() { Id = id, Sku = sku, Name = "Позиция " + sku, UnitId = 1, Unit = unit };
        stock.Items.AddRange([ItemOf(1, "23147"), ItemOf(2, "23148"), ItemOf(3, "99999")]);
        stock.Batches.AddRange([new Batch { Id = 1, ItemId = 1, UnitPrice = 60 }, new Batch { Id = 2, ItemId = 2, UnitPrice = 10 },
                                new Batch { Id = 3, ItemId = 3, UnitPrice = null }]);
        stock.Balances.AddRange([
            new StockBalance { ItemId = 1, LocationId = 1, BatchId = 1, Quantity = 12 },
            new StockBalance { ItemId = 2, LocationId = 1, BatchId = 2, Quantity = 3 },
            new StockBalance { ItemId = 3, LocationId = 1, BatchId = 3, Quantity = 1 }]);
        var service = new ReconcileService(stock, new EmployeeService(new FakeEmployeeRepository()));

        SaldoBatch B(string code, decimal qty, decimal value) => new("10.6", "Отдел", "Дорофей С.М.", code, "Позиция " + code, "шт", "ТТН", null, qty, value);
        var result = await service.ReconcileAsync([new SaldoSheet("склад.xlsx", "31.10.2026",
            [B("0000023147", 12, 720), B("23148", 5, 50), B("55555", 2, 20)])], "дорофей с.м.");

        Assert.Equal("Дорофей С.М.", result.WarehouseMol);
        ReconcileStatus StatusOf(string code) => result.Rows.Single(r => ReconcileService.Key(r.Code) == code).Status;
        Assert.Equal(ReconcileStatus.Match, StatusOf("23147"));      // 0000023147 = 23147
        Assert.Equal(ReconcileStatus.QtyDiffers, StatusOf("23148"));
        Assert.Equal(ReconcileStatus.OnlyIn1C, StatusOf("55555"));
        Assert.Equal(ReconcileStatus.OnlyInApp, StatusOf("99999"));
        Assert.Equal(ReconcileStatus.QtyDiffers, result.Rows[0].Status); // расхождения — первыми
    }
}
