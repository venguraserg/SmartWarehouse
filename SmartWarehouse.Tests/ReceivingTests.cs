using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;
using SmartWarehouse.Infrastructure.Import;

namespace SmartWarehouse.Tests;

public class ReceivingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeCatalogRepository _catalog = new();
    private readonly FakeLocationRepository _locations = new();
    private readonly FakeStockRepository _stock = new();
    private readonly ReceivingService _service;

    public ReceivingTests()
    {
        _catalog.Units.Add(new UnitOfMeasure { Id = 1, Code = "шт", Name = "штука" });
        _catalog.Units.Add(new UnitOfMeasure { Id = 2, Code = "м", Name = "метр", IsFractional = true });
        _locations.Warehouses.Add(new Warehouse { Id = 1, Code = "WH1", Name = "Склад" });
        var clock = new FixedTimeProvider(Now);
        _service = new ReceivingService(new CatalogService(_catalog, clock), new LocationService(_locations),
            new StockService(_stock, clock), _stock, clock);
    }

    private static InvoiceFile Invoice(params InvoiceLine[] lines) =>
        new("0000016", new DateOnly(2026, 11, 30), "10.6", lines, []);

    // ---------- Чтение накладной ----------

    [Fact]
    public void Накладная_xls_из_1С_читается()
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "invoice-1c.xls"));

        var invoice = new ExcelImportReader().ReadInvoice(stream, "invoice-1c.xls");

        Assert.Equal("0000016", invoice.Number);
        Assert.Equal(new DateOnly(2026, 11, 30), invoice.Date);
        Assert.Equal("10.6", invoice.Account);
        Assert.Empty(invoice.Issues);
        Assert.Equal(5, invoice.Lines.Count); // без строки с номерами граф и «Итого»
        var first = invoice.Lines[0];
        Assert.Equal(("0000000917", "Подшипник 6205", "шт", 12.5m, 4m, 50m), (first.Code, first.Name, first.Unit, first.Price, first.Quantity, first.Amount));
        Assert.Equal(1060m, invoice.Lines[3].Quantity);  // «1 060,0000» текстом
        Assert.Equal("м.п.", invoice.Lines[3].Unit);
        Assert.Equal(0.142m, invoice.Lines[4].Quantity);
    }

    [Theory]
    [InlineData("м.п.", "м")]
    [InlineData("пог. м", "м")]
    [InlineData("тн", "т")]
    [InlineData("ШТ", "шт")]
    [InlineData("Комплект", "компл")]
    [InlineData(" шт/м3 ", "шт/м3")]
    public void Единицы_1С_приводятся_к_одному_виду(string unit, string expected) =>
        Assert.Equal(expected, UnitAliases.Normalize(unit));

    [Theory]
    [InlineData("м", true)]
    [InlineData("10 шт", true)]
    [InlineData("25 шт", true)]
    [InlineData("100 шт", true)]
    [InlineData("10 рул", true)]
    [InlineData("шт", false)]
    [InlineData("упак", false)]
    public void Дробное_количество_в_упаковках_N_шт(string unit, bool fractional) =>
        Assert.Equal(fractional, UnitAliases.IsFractional(unit));

    // ---------- Проверка накладной ----------

    [Fact]
    public async Task Проверка_накладной_объединяет_строки_и_сопоставляет_по_коду()
    {
        _catalog.Items.Add(new Item { Id = 5, Sku = "1205", Name = "Кабель ВВГ", UnitId = 2, Unit = _catalog.Units[1] });

        var result = await _service.ImportInvoiceAsync(Invoice(
            new InvoiceLine(16, "0000000917", "Подшипник 6205", "шт", 12.5m, 4, 50),
            new InvoiceLine(17, "0000000917", "Подшипник 6205", "шт", 12.5m, 6, 75),
            new InvoiceLine(18, "0000000917", "Подшипник 6205", "шт", 14m, 2, 28),      // другая цена — своя партия
            new InvoiceLine(19, "0000001205", "Кабель ВВГ 3х2,5", "м.п.", 3.2m, 1060, 3392),
            new InvoiceLine(20, "0000000300", "Болт", "шт", 1m, 1.5m, 1.5m),             // дробное в штуках
            new InvoiceLine(21, "0000000301", "Гайка", "шт", null, 2, null)),            // нет цены
            warehouseId: 1, apply: false, new ImportActor(null, "Кладовщик"));

        Assert.False(result.Applied);
        Assert.Equal("Накладная № 0000016 от 30.11.2026", result.Title);
        Assert.Equal("WH1-RCV", result.ReceptionCode);
        Assert.Equal(3, result.Batches.Count);
        var bearing = result.Batches[0];
        Assert.Equal(("917", 10m, 12.5m, 125m, true), (bearing.Code, bearing.Quantity, bearing.Price, bearing.Amount, bearing.NewItem));
        Assert.Equal((2m, 14m), (result.Batches[1].Quantity, result.Batches[1].Price));
        var cable = result.Batches[2];
        Assert.Equal(("1205", "Кабель ВВГ", "м", false), (cable.Code, cable.Name, cable.Unit, cable.NewItem)); // позиция уже есть: 0000001205 = 1205
        Assert.Equal(1, result.ItemsNew);
        Assert.Equal([20, 21], result.Issues.Select(i => i.Line));
        Assert.Equal(125m + 28m + 3392m, result.Value);
        Assert.Single(_catalog.Items); // проверка ничего не создаёт
    }

    // ---------- Кому и от кого: МОЛ — кладовщик ----------

    [Fact]
    public void В_накладной_читаются_кому_отпустил_и_склады()
    {
        using var book = new ClosedXML.Excel.XLWorkbook();
        var sheet = book.AddWorksheet("TDSheet");
        void Put(string cell, object value)
        {
            if (value is string text)
                sheet.Cell(cell).Value = text;
            else
                sheet.Cell(cell).Value = Convert.ToDouble(value);
        }
        Put("A3", "НАКЛАДНАЯ НА ВНУТРЕННЕЕ ПЕРЕМЕЩЕНИЕ МАТЕРИАЛОВ");
        Put("B5", "Номер документа"); Put("D5", "Дата составле- ния"); Put("B7", "in-0832094"); Put("D7", "17.11.2026");
        Put("A11", "Со склада"); Put("C11", "Центральный склад"); Put("M11", "На склад"); Put("Q11", "Отдел эксплуатации");
        Put("A13", "Кому"); Put("C13", "Иванов И.П."); Put("M13", "Через кого"); Put("Q13", "Сидоров О.");
        Put("A15", "№ п/п"); Put("B15", "Материальные ценности"); Put("J15", "Единица измер."); Put("M15", "Цена, руб. коп.");
        Put("S15", "Количество"); Put("Z15", "Сумма, руб. коп."); Put("G16", "Номенклатур- ный номер");
        Put("A17", 1); Put("B17", "Аргон газообразный"); Put("G17", "0000013024"); Put("J17", "м3"); Put("M17", 4.77); Put("S17", 50.4); Put("Z17", 240.41);
        Put("A18", "Итого:");
        Put("A20", "Отпустил ___________________"); Put("F20", "Гость О.И."); Put("M20", "Получил ___________________"); Put("X20", "Иванов И.П.");
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        stream.Position = 0;

        var invoice = new ExcelImportReader().ReadInvoice(stream, "n.xlsx");

        Assert.Equal(("in-0832094", "Иванов И.П.", "Гость О.И.", "Центральный склад", "Отдел эксплуатации"),
            (invoice.Number, invoice.Recipient, invoice.Sender, invoice.FromStore, invoice.ToStore));
        Assert.Equal(50.4m, Assert.Single(invoice.Lines).Quantity);
    }

    /// <summary>Кладовщики Иванов (1) и Петров (2); у Иванова на складе 10 подшипников. Гость — кладовщик центрального склада.</summary>
    private ReceivingService WithStorekeepers()
    {
        var people = new[]
        {
            new Employee { Id = 1, LastName = "Иванов", FirstName = "Иван", MiddleName = "Петрович" },
            new Employee { Id = 2, LastName = "Петров", FirstName = "Сергей", MiddleName = "Васильевич" },
            new Employee { Id = 3, LastName = "Сидоров", FirstName = "Олег" }
        };
        _stock.Employees.AddRange(people);
        _stock.Storekeepers.AddRange([1, 2]);
        var employees = new FakeEmployeeRepository();
        employees.Employees.AddRange(people);
        var item = new Item { Id = 7, Sku = "917", Name = "Подшипник 6205", UnitId = 1, Unit = _catalog.Units[0] };
        _catalog.Items.Add(item);
        _stock.Items.Add(item);
        _stock.Locations.Add(new StorageLocation { Id = 10, WarehouseId = 1, Code = "1", FullCode = "WH1-1" });
        _stock.Batches.Add(new Batch { Id = 1, ItemId = 7, UnitPrice = 12.5m, MolId = 1, ReceivedAt = Now });
        _stock.Balances.Add(new StockBalance { ItemId = 7, LocationId = 10, BatchId = 1, Quantity = 10 });
        var clock = new FixedTimeProvider(Now);
        return new ReceivingService(new CatalogService(_catalog, clock), new LocationService(_locations),
            new StockService(_stock, clock), _stock, clock, new EmployeeService(employees),
            new StubMolService(new MolDto(1, "Иванов И. П."), new MolDto(2, "Петров С. В.")));
    }

    private static InvoiceFile From(string? sender, string? recipient, decimal qty) =>
        new("in-1", new DateOnly(2026, 11, 24), "10.6", [new InvoiceLine(18, "0000000917", "Подшипник 6205", "шт", 12.5m, qty, qty * 12.5m)], [],
            recipient, sender);

    [Fact]
    public async Task Приход_только_на_кладовщика_партии_за_ним()
    {
        var service = WithStorekeepers();

        var check = await service.ImportInvoiceAsync(From("Гость О.И.", "Петров С.В.", 3), 1, apply: false, new ImportActor(null, null));
        Assert.Equal(("Петров С. В.", "Гость О.И.", false), (check.Recipient, check.Sender, check.MolChange));

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            service.ImportInvoiceAsync(From("Гость О.И.", "Сидоров О.", 3), 1, apply: false, new ImportActor(null, null)));
        Assert.Contains("не кладовщик", ex.Message);
    }

    [Fact]
    public async Task Накладная_между_кладовщиками_меняет_МОЛ_без_движения_товара()
    {
        var service = WithStorekeepers();

        var check = await service.ImportInvoiceAsync(From("Иванов И.П.", "Петров С.В.", 4), 1, apply: false, new ImportActor(null, null));
        Assert.True(check.MolChange);
        Assert.Empty(check.Issues);

        var done = await service.ImportInvoiceAsync(From("Иванов И.П.", "Петров С.В.", 4), 1, apply: true, new ImportActor(1, null));
        Assert.True(done.Applied);
        Assert.Equal(10, _stock.Balances.Where(b => b.LocationId == 10).Sum(b => b.Quantity));          // на месте
        Assert.Equal([(1, 6m), (2, 4m)], _stock.Balances.OrderBy(b => b.BatchId)
            .Select(b => (_stock.Batches.Single(x => x.Id == b.BatchId).MolId!.Value, b.Quantity)));
        var doc = _stock.Documents.Single(d => d.Type == MovementType.MolChange);
        Assert.Equal("Накладная № in-1 от 24.11.2026", doc.ExternalNumber);

        // Повторно — нельзя; больше, чем есть у отправителя, — не проводится.
        await Assert.ThrowsAsync<DomainException>(() => service.ImportInvoiceAsync(From("Иванов И.П.", "Петров С.В.", 4), 1, apply: true, new ImportActor(1, null)));
        var tooMuch = From("Иванов И.П.", "Петров С.В.", 7) with { Number = "in-2" };
        Assert.Single((await service.ImportInvoiceAsync(tooMuch, 1, apply: false, new ImportActor(null, null))).Issues);
        await Assert.ThrowsAsync<DomainException>(() => service.ImportInvoiceAsync(tooMuch, 1, apply: true, new ImportActor(1, null)));
    }

    [Fact]
    public async Task Повторная_накладная_не_принимается()
    {
        _stock.Documents.Add(new StockDocument { Id = 1, Type = MovementType.Receipt, ExternalNumber = "Накладная № 0000016 от 30.11.2026" });
        var invoice = Invoice(new InvoiceLine(16, "917", "Подшипник", "шт", 12.5m, 4, 50));

        var check = await _service.ImportInvoiceAsync(invoice, 1, apply: false, new ImportActor(null, null));
        Assert.True(check.AlreadyImported);

        await Assert.ThrowsAsync<DomainException>(() => _service.ImportInvoiceAsync(invoice, 1, apply: true, new ImportActor(null, null)));
    }

    [Fact]
    public async Task Накладная_без_номера_или_даты_отклоняется()
    {
        var invoice = new InvoiceFile(null, null, null, [new InvoiceLine(16, "917", "Подшипник", "шт", 1, 1, 1)], []);

        await Assert.ThrowsAsync<DomainException>(() => _service.ImportInvoiceAsync(invoice, 1, apply: false, new ImportActor(null, null)));
    }

    [Fact]
    public async Task Предупреждение_о_возможном_задвоении_с_ручным_учётным_приходом()
    {
        var unit = _catalog.Units[0];
        var item = new Item { Id = 7, Sku = "917", Name = "Подшипник 6205", UnitId = 1, Unit = unit };
        _catalog.Items.Add(item);
        _stock.Items.Add(item);
        _stock.Batches.Add(new Batch { Id = 1, ItemId = 7, UnitPrice = 12.5m, ReceiptDocumentId = 3, Origin = BatchOrigin.Manual });
        _stock.Balances.Add(new StockBalance { ItemId = 7, LocationId = 1, BatchId = 1, Quantity = 4 });

        var result = await _service.ImportInvoiceAsync(Invoice(new InvoiceLine(16, "0000000917", "Подшипник 6205", "шт", 12.5m, 4, 50)),
            1, apply: false, new ImportActor(null, null));

        Assert.Contains("вручную", Assert.Single(result.Batches).Warning);
    }

    // ---------- Расхождения ----------

    [Fact]
    public async Task Расхождение_с_накладной_записывается_и_закрывается()
    {
        var unit = _catalog.Units[0];
        _stock.Items.Add(new Item { Id = 7, Sku = "917", Name = "Подшипник 6205", UnitId = 1, Unit = unit });
        _stock.Batches.Add(new Batch { Id = 1, ItemId = 7, UnitPrice = 12.5m, Note = "Накладная № 16 от 30.11.2026", Origin = BatchOrigin.Invoice });
        var doc = new StockDocument { Id = 1, Type = MovementType.Receipt };
        doc.Lines.Add(new StockMovement { ItemId = 7, BatchId = 1, Quantity = 10 });
        _stock.Documents.Add(doc);

        var d = await _service.ReportDiscrepancyAsync(new ReportDiscrepancyRequest(1, 8, " недовоз "), "Кладовщик");

        Assert.Equal((10m, 8m, "недовоз", "917"), (d.Expected, d.Actual, d.Comment, d.Sku));
        Assert.Single(await _service.ListDiscrepanciesAsync(false));
        await Assert.ThrowsAsync<DomainException>(() => _service.ReportDiscrepancyAsync(new ReportDiscrepancyRequest(1, 10, null), null)); // расхождения нет

        var closed = await _service.ResolveDiscrepancyAsync(d.Id, "довезли");
        Assert.Equal(Now, closed.ResolvedAt);
        Assert.Empty(await _service.ListDiscrepanciesAsync(false));
        Assert.Single(await _service.ListDiscrepanciesAsync(true));
        await Assert.ThrowsAsync<DomainException>(() => _service.ResolveDiscrepancyAsync(d.Id, null));
    }

    // ---------- «Сортировка» при вводе остатков ----------

    [Fact]
    public void Примечание_партии_в_Сортировке_хранит_прежнее_место()
    {
        OpeningRow Row(string? address) =>
            new(3, "10.6", "70002", "Позиция", "шт", 2, 20, "ТТН 7", new DateOnly(2026, 5, 27), false, null, address, null, null);

        Assert.Equal("Прежнее место: 2C.2.3.1,34; ТТН 7 от 27.05.2026", OpeningImportService.SortingNote(Row(" 2C.2.3.1,34 ")));
        Assert.Equal("Прежнее место: шкаф свар; ТТН 7 от 27.05.2026", OpeningImportService.SortingNote(Row("шкаф  свар")));
        Assert.StartsWith("Прежнее место: не указано", OpeningImportService.SortingNote(Row(null)));
        Assert.True(OpeningImportService.SortingNote(Row(new string('x', 400))).Length <= 256);
    }

    [Fact]
    public async Task Товар_из_Сортировки_в_списке_к_размещению()
    {
        _locations.Locations.Add(new StorageLocation { Id = 50, WarehouseId = 1, Type = LocationType.Cell, Code = "SORT", FullCode = "WH1-SORT", Name = OpeningImportService.SortingName });
        var unit = _catalog.Units[0];
        _stock.Items.Add(new Item { Id = 7, Sku = "70002", Name = "Подшипник", UnitId = 1, Unit = unit });
        _stock.Batches.Add(new Batch { Id = 1, ItemId = 7, UnitPrice = 10, Note = "Прежнее место: шкаф свар", Origin = BatchOrigin.Opening });
        _stock.Balances.Add(new StockBalance { ItemId = 7, LocationId = 50, BatchId = 1, Quantity = 2 });

        var p = Assert.Single(await _service.ListPendingAsync());

        Assert.True(p.Sorting);
        Assert.Equal(("WH1-SORT", "Прежнее место: шкаф свар", 2m), (p.LocationCode, p.Invoice, p.Quantity));
    }
}
