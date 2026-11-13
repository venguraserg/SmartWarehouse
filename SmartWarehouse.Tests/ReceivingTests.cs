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
}
