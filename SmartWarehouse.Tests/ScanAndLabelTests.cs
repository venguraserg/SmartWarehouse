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
        var warehouse = new Warehouse { Id = 1, Code = "WH1", Name = "Склад запчастей" };
        _locations.Warehouses.Add(warehouse);
        _locations.Locations.Add(new StorageLocation { Id = 10, WarehouseId = 1, Warehouse = warehouse, Type = LocationType.Rack, Code = "A", FullCode = "WH1-A" });
        _locations.Locations.Add(new StorageLocation { Id = 11, WarehouseId = 1, Warehouse = warehouse, ParentId = 10, Type = LocationType.Cell, Code = "01", FullCode = "WH1-A-01", Name = "Подшипники" });

        var pcs = new UnitOfMeasure { Id = 1, Code = "шт", Name = "штука" };
        _catalog.Units.Add(pcs);
        var bearing = new Item { Id = 5, Sku = "SP-000005", Name = "Подшипник 6205-2RS", UnitId = 1, Unit = pcs, Barcode = "4607001234567" };
        _catalog.Items.Add(bearing);
        _stock.Items.Add(bearing);

        _scan = new ScanService(_locations, _catalog);
        _settings = new SettingsService(_settingsRepo, new DocumentPrintOptions(null), new FixedTimeProvider(DateTimeOffset.UnixEpoch));
        _labels = new LabelService(_locations, _stock, new LabelPdfRenderer(), _settings);
    }

    // ---------- Распознавание кода ----------

    [Theory]
    [InlineData("WH1-A-01")]
    [InlineData("СК1-A-01")]                                   // старое написание (до перевода в латиницу)
    [InlineData(" ск1-a-01 ")]
    [InlineData("http://192.168.1.10:8081/l/%D0%A1%D0%9A1-A-01")]
    [InlineData("https://sklad.local/l/СК1-A-01")]
    public async Task Ячейка_по_коду_и_по_ссылке_с_наклейки(string code)
    {
        var found = await _scan.ResolveAsync(code);

        Assert.Equal(ScanKind.Location, found.Kind);
        Assert.Equal(11, found.Id);
        Assert.Equal("WH1-A-01", found.Code);
    }

    [Theory]
    [InlineData("SP-000005")]
    [InlineData("ЗЧ-000005")]                                  // старый артикул
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

    // ---------- Латиница в кодах ----------

    [Theory]
    [InlineData("СК1-A-01", "WH1-A-01")]
    [InlineData("ск2-Б-03", "WH2-B-03")]
    [InlineData("ЗЧ-000005", "SP-000005")]
    [InlineData("ЖУК-Щ1", "ZHUK-SHCH1")]
    [InlineData("A-01", "A-01")]
    public void Старые_коды_переводятся_в_латиницу(string code, string expected) =>
        Assert.Equal(expected, Transliteration.ToLatin(code).ToUpperInvariant());

    // ---------- Первые наклейки с короткими кодами SWL / SWI ----------

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
        Assert.Equal("WH1-A-01", found.Code);
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

        Assert.Equal(["WH1-A-01", "SP-000005"], _captured.Labels.Select(l => l.Payload));
        Assert.Equal("WH1-A-01", _captured.Labels[0].Code); // крупная подпись — как раньше
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

        Assert.Equal("http://site:8081/l/WH1-A-01", _captured.Labels[0].Payload);
        Assert.Equal("http://192.168.1.10:8081/i/SP-000005", _captured.Labels[1].Payload);
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

    /// <summary>1-й этаж › стеллаж C3: полки 0 (пол), 1 (ячейки 1, 2), 2 («ремни»).</summary>
    private void AddRackC3()
    {
        var wh = _locations.Warehouses[0];
        void Add(int id, int? parent, LocationType type, string code, string full, string? name = null) =>
            _locations.Locations.Add(new StorageLocation { Id = id, WarehouseId = 1, Warehouse = wh, ParentId = parent, Type = type, Code = code, FullCode = full, Name = name });
        Add(19, null, LocationType.Zone, "1", "WH1-1");
        Add(20, 19, LocationType.Rack, "C3", "WH1-1-C3", "Подшипники, ремни");
        Add(21, 20, LocationType.Shelf, "0", "WH1-1-C3-0");
        Add(22, 20, LocationType.Shelf, "1", "WH1-1-C3-1");
        Add(23, 20, LocationType.Shelf, "2", "WH1-1-C3-2", "ремни");
        Add(25, 22, LocationType.Cell, "2", "WH1-1-C3-1-2");
        Add(24, 22, LocationType.Cell, "1", "WH1-1-C3-1-1", "Подшипники SKF");
    }

    [Fact]
    public async Task Наклейка_места_с_адресом_словами()
    {
        AddRackC3();
        var labels = new LabelService(_locations, _stock, _captured, _settings);

        await labels.RenderAsync(new LabelRequest(LabelKind.Location, [24, 21], null));

        var cell = _captured.Labels.Single(l => l.Code == "WH1-1-C3-1-1");
        Assert.Equal("C3 · п.1 · яч.1", cell.Big);
        Assert.Equal("1-й этаж", cell.Area);
        Assert.Equal("Подшипники SKF", cell.Title);
        Assert.Equal("C3 · пол", _captured.Labels.Single(l => l.Code == "WH1-1-C3-0").Big);
    }

    [Fact]
    public async Task Стеллаж_табличка_и_наклейки_сверху_вниз()
    {
        AddRackC3();
        var labels = new LabelService(_locations, _stock, _captured, _settings);

        var (_, fileName) = await labels.RenderAsync(new LabelRequest(LabelKind.Rack, [20], "3x8"));

        Assert.Equal("Стеллаж_C3.pdf", fileName);
        var sign = Assert.Single(_captured.Signs);
        Assert.Equal("C3", sign.Rack);
        Assert.Equal("1-й этаж", sign.Area);
        Assert.Equal("Подшипники, ремни", sign.Note);
        Assert.Equal([("п.2", "ремни"), ("п.1", "яч. 1–2"), ("пол", "под стеллажом")],
            sign.Shelves.Select(x => (x.Label, x.Hint)));
        // Полка 2 — своя наклейка; на полке 1 — только ячейки; пол — последним.
        Assert.Equal(["WH1-1-C3-2", "WH1-1-C3-1-1", "WH1-1-C3-1-2", "WH1-1-C3-0"], _captured.Labels.Select(l => l.Code));

        _captured.Signs.Clear();
        _captured.Labels.Clear();
        await labels.RenderAsync(new LabelRequest(LabelKind.Rack, [20], "3x8", Sign: false));
        Assert.Empty(_captured.Signs);
        Assert.Equal(4, _captured.Labels.Count);

        await Assert.ThrowsAsync<DomainException>(() => labels.RenderAsync(new LabelRequest(LabelKind.Rack, [24], "3x8")));
        await Assert.ThrowsAsync<DomainException>(() => labels.RenderAsync(new LabelRequest(LabelKind.Rack, [20], "3x8", Sign: false, Stickers: false)));
    }

    [Theory]
    [InlineData("3x8")]
    [InlineData("4x10")]
    public async Task Стеллаж_в_PDF_табличка_A5_и_лист_наклеек(string layout)
    {
        AddRackC3();
        var (pdf, _) = await _labels.RenderAsync(new LabelRequest(LabelKind.Rack, [20], layout));

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
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
    public List<RackSignData> Signs { get; } = [];

    public byte[] Render(IReadOnlyList<LabelData> labels, LabelLayout layout, int skip)
    {
        Labels.AddRange(labels);
        return [1];
    }

    public byte[] Render(IReadOnlyList<RackSignData> signs, IReadOnlyList<LabelData> labels, LabelLayout layout, int skip)
    {
        Signs.AddRange(signs);
        return Render(labels, layout, skip);
    }
}
