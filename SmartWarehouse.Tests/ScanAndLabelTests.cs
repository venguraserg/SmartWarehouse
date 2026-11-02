using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;
using SmartWarehouse.Infrastructure.Pdf;

namespace SmartWarehouse.Tests;

public class ScanAndLabelTests
{
    private readonly FakeLocationRepository _locations = new();
    private readonly FakeCatalogRepository _catalog = new();
    private readonly FakeStockRepository _stock = new();
    private readonly FakeSettingsRepository _settingsRepo = new();
    private readonly CapturingLabelRenderer _captured = new();
    private readonly SettingsService _settings;
    private readonly ScanService _scan;
    private readonly LabelService _labels;

    public ScanAndLabelTests()
    {
        var warehouse = new Warehouse { Id = 1, Code = "СК1", Name = "Склад запчастей" };
        _locations.Warehouses.Add(warehouse);
        _locations.Locations.Add(new StorageLocation { Id = 10, WarehouseId = 1, Warehouse = warehouse, Type = LocationType.Rack, Code = "A", FullCode = "СК1-A" });
        _locations.Locations.Add(new StorageLocation { Id = 11, WarehouseId = 1, Warehouse = warehouse, ParentId = 10, Type = LocationType.Cell, Code = "01", FullCode = "СК1-A-01", Name = "Подшипники" });

        var pcs = new UnitOfMeasure { Id = 1, Code = "шт", Name = "штука" };
        _catalog.Units.Add(pcs);
        var bearing = new Item { Id = 5, Sku = "ЗЧ-000005", Name = "Подшипник 6205-2RS", UnitId = 1, Unit = pcs, Barcode = "4607001234567" };
        _catalog.Items.Add(bearing);
        _stock.Items.Add(bearing);

        _scan = new ScanService(_locations, _catalog);
        _settings = new SettingsService(_settingsRepo, new DocumentPrintOptions(null), new FixedTimeProvider(DateTimeOffset.UnixEpoch));
        _labels = new LabelService(_locations, _stock, new LabelPdfRenderer(), _settings);
    }

    // ---------- Распознавание кода ----------

    [Theory]
    [InlineData("СК1-A-01")]
    [InlineData(" ск1-a-01 ")]
    [InlineData("http://192.168.1.10:8081/l/%D0%A1%D0%9A1-A-01")]
    [InlineData("https://sklad.local/l/СК1-A-01")]
    public async Task Ячейка_по_коду_и_по_ссылке_с_наклейки(string code)
    {
        var found = await _scan.ResolveAsync(code);

        Assert.Equal(ScanKind.Location, found.Kind);
        Assert.Equal(11, found.Id);
        Assert.Equal("СК1-A-01", found.Code);
    }

    [Theory]
    [InlineData("ЗЧ-000005")]
    [InlineData("зч-000005")]
    [InlineData("4607001234567")]
    [InlineData("http://sklad:8081/i/%D0%97%D0%A7-000005")]
    public async Task Позиция_по_артикулу_штрих_коду_и_ссылке(string code)
    {
        var found = await _scan.ResolveAsync(code);

        Assert.Equal(ScanKind.Item, found.Kind);
        Assert.Equal(5, found.Id);
    }

    [Fact]
    public async Task Неизвестный_код_не_найден()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _scan.ResolveAsync("0000000000000"));
        await Assert.ThrowsAsync<NotFoundException>(() => _scan.ResolveAsync("http://sklad/l/СК9-Z"));
        await Assert.ThrowsAsync<DomainException>(() => _scan.ResolveAsync("   "));
    }

    [Fact]
    public void Ссылка_с_наклейки_строится_и_разбирается()
    {
        var link = LabelLinks.ForLocation("http://sklad:8081/", "СК1-A-01");
        Assert.Equal("http://sklad:8081/l/%D0%A1%D0%9A1-A-01", link);

        Assert.True(LabelLinks.TryParse(link, out var kind, out var code));
        Assert.Equal(ScanKind.Location, kind);
        Assert.Equal("СК1-A-01", code);

        // Без адреса — путь, тоже только латиницей.
        var path = LabelLinks.ForItem(null, "ЗЧ-000005");
        Assert.Equal("/i/%D0%97%D0%A7-000005", path);
        Assert.True(path.All(char.IsAscii));
        Assert.True(LabelLinks.TryParse(path, out kind, out code));
        Assert.Equal(ScanKind.Item, kind);
        Assert.Equal("ЗЧ-000005", code);

        Assert.False(LabelLinks.TryParse("СК1-A-01", out _, out _));
    }

    // ---------- Короткие коды наклеек (без ссылки) ----------

    [Theory]
    [InlineData("SWL-11")]
    [InlineData("swl-11")]       // Caps Lock
    [InlineData("ЫЦД-11")]       // русская раскладка
    [InlineData(" SWL-11 ")]
    public async Task Ячейка_по_короткому_коду(string code)
    {
        var found = await _scan.ResolveAsync(code);

        Assert.Equal(ScanKind.Location, found.Kind);
        Assert.Equal(11, found.Id);
        Assert.Equal("СК1-A-01", found.Code);
    }

    [Fact]
    public async Task Позиция_по_короткому_коду_и_неизвестный_код()
    {
        var found = await _scan.ResolveAsync("SWI-5");
        Assert.Equal(ScanKind.Item, found.Kind);
        Assert.Equal(5, found.Id);

        var missing = await Assert.ThrowsAsync<NotFoundException>(() => _scan.ResolveAsync("SWL-999"));
        Assert.Contains("наклейки", missing.Message);
        Assert.False(LabelCodes.TryParse("SWL-", out _, out _));
        Assert.False(LabelCodes.TryParse("SWL-1a", out _, out _));
        Assert.False(LabelCodes.TryParse("SWL--1", out _, out _));
    }

    [Fact]
    public async Task В_QR_по_умолчанию_только_код_без_ссылки()
    {
        var labels = new LabelService(_locations, _stock, _captured, _settings);

        await labels.RenderAsync(new LabelRequest(LabelKind.Location, [11], null, BaseUrl: "http://sklad:8081"));
        await labels.RenderAsync(new LabelRequest(LabelKind.Item, [5], null, BaseUrl: "http://sklad:8081"));

        Assert.Equal(["SWL-11", "SWI-5"], _captured.Labels.Select(l => l.Payload));
        Assert.Equal("СК1-A-01", _captured.Labels[0].Code); // крупная подпись — как раньше
        Assert.All(_captured.Labels, l => Assert.True(l.Payload.All(char.IsAscii)));
    }

    [Fact]
    public async Task Ссылка_в_QR_с_адресом_из_настроек_или_сайта()
    {
        var labels = new LabelService(_locations, _stock, _captured, _settings);

        await _settings.SaveLabelSettingsAsync(new LabelSettingsDto(QrContent.Link, null), "Админ");
        await labels.RenderAsync(new LabelRequest(LabelKind.Location, [11], null, BaseUrl: "http://site:8081"));

        await _settings.SaveLabelSettingsAsync(new LabelSettingsDto(QrContent.Link, " http://192.168.1.10:8081/ "), "Админ");
        await labels.RenderAsync(new LabelRequest(LabelKind.Item, [5], null, BaseUrl: "http://site:8081"));

        Assert.Equal("http://site:8081/l/%D0%A1%D0%9A1-A-01", _captured.Labels[0].Payload);
        Assert.Equal("http://192.168.1.10:8081/i/%D0%97%D0%A7-000005", _captured.Labels[1].Payload);
    }

    // ---------- Наклейки ----------

    [Fact]
    public async Task Наклейки_строятся_в_PDF_на_нескольких_листах()
    {
        // 24 на листе, пропускаем 20 — 2 ячейки × 3 копии = 6 наклеек уходят на второй лист.
        var (pdf, fileName) = await _labels.RenderAsync(new LabelRequest(LabelKind.Location, [10, 11], "3x8", Skip: 20, BaseUrl: "http://sklad:8081", Copies: 3));

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.EndsWith(".pdf", fileName);
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(text, @"/Type\s*/Page[^s]").Count);
    }

    [Theory]
    [InlineData("3x7")]
    [InlineData("4x10")]
    [InlineData("2x5")]
    public async Task Наклейки_номенклатуры_для_всех_раскладок(string layout)
    {
        var (pdf, _) = await _labels.RenderAsync(new LabelRequest(LabelKind.Item, [5], layout));
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public async Task Неверные_параметры_наклеек_отклоняются()
    {
        await Assert.ThrowsAsync<DomainException>(() => _labels.RenderAsync(new LabelRequest(LabelKind.Item, [], null)));
        await Assert.ThrowsAsync<DomainException>(() => _labels.RenderAsync(new LabelRequest(LabelKind.Item, [5], "9x9")));
        await Assert.ThrowsAsync<DomainException>(() => _labels.RenderAsync(new LabelRequest(LabelKind.Item, [5], "3x8", Skip: 24)));
        await Assert.ThrowsAsync<DomainException>(() => _labels.RenderAsync(new LabelRequest(LabelKind.Item, [5], null, BaseUrl: "ftp://x")));
        await Assert.ThrowsAsync<NotFoundException>(() => _labels.RenderAsync(new LabelRequest(LabelKind.Location, [999], null)));
    }

    [Fact]
    public void Адрес_приложения_приводится_к_виду_без_лишнего()
    {
        Assert.Equal("http://sklad:8081", LabelService.NormalizeBaseUrl("http://sklad:8081/"));
        Assert.Equal("https://host/app", LabelService.NormalizeBaseUrl("https://host/app/?x=1"));
        Assert.Null(LabelService.NormalizeBaseUrl(" "));
    }

    // ---------- Ручной сканер ----------

    [Theory]
    [InlineData("реезЖ..ылдфв.д.%В0%Ф1%В0%9Ф1-Ф-01")]   // http://sklad/l/%D0%A1%D0%9A1-A-01 в русской раскладке
    [InlineData("HTTP://SKLAD:8081/L/%D0%A1%D0%9A1-A-01")]  // Caps Lock
    [InlineData("/l/%D0%A1%D0%9A1-A-01")]                    // наклейка без адреса
    public async Task Ячейка_распознаётся_при_неверной_раскладке_и_Caps_Lock(string typed)
    {
        var found = await _scan.ResolveAsync(typed);

        Assert.Equal(ScanKind.Location, found.Kind);
        Assert.Equal(11, found.Id);
    }

    [Fact]
    public async Task Штрих_код_и_артикул_в_русской_раскладке()
    {
        _catalog.Items.Add(new Item { Id = 6, Sku = "BRG-6205", Name = "Подшипник", UnitId = 1 });

        Assert.Equal(6, (await _scan.ResolveAsync("ИКП-6205")).Id);  // BRG-6205
        Assert.Equal(5, (await _scan.ResolveAsync("зч-000005")).Id); // настоящая кириллица — находится сразу
    }

    [Fact]
    public void Перевод_раскладки()
    {
        Assert.Equal("http://sklad/l/A-01", KeyboardLayout.FromRussianLayout("реезЖ..ылдфв.д.Ф-01"));
        Assert.True(KeyboardLayout.HasRussianLetters("реез"));
        Assert.False(KeyboardLayout.HasRussianLetters("http://x/l/A"));
    }
}

internal class CapturingLabelRenderer : ILabelPdfRenderer
{
    public List<LabelData> Labels { get; } = [];

    public byte[] Render(IReadOnlyList<LabelData> labels, LabelLayout layout, int skip)
    {
        Labels.AddRange(labels);
        return [1];
    }
}
