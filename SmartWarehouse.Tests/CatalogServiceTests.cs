using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Tests;

public class CatalogServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2];
    private static readonly byte[] Webp = [0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50, 1];

    private readonly FakeCatalogRepository _repo = new();
    private readonly CatalogService _service;

    public CatalogServiceTests()
    {
        _repo.Units.Add(new UnitOfMeasure { Id = 1, Code = "шт", Name = "штука" });
        _repo.Units.Add(new UnitOfMeasure { Id = 2, Code = "м", Name = "метр", IsFractional = true });
        _service = new CatalogService(_repo, new FixedTimeProvider(Now));
    }

    private Task<ItemDto> CreateItemAsync(string sku = "BRG-1") =>
        _service.CreateItemAsync(new CreateItemRequest(sku, "Подшипник", null, 1, 5, 10, null));

    // ---------- Формат изображения ----------

    [Fact]
    public void Формат_определяется_по_содержимому()
    {
        Assert.Equal("image/jpeg", ImageRules.DetectContentType(Jpeg));
        Assert.Equal("image/png", ImageRules.DetectContentType(Png));
        Assert.Equal("image/webp", ImageRules.DetectContentType(Webp));
    }

    [Fact]
    public void Не_изображение_и_слишком_большой_файл_отклоняются()
    {
        Assert.Throws<DomainException>(() => ImageRules.DetectContentType([]));
        Assert.Throws<DomainException>(() => ImageRules.DetectContentType("%PDF-1.7"u8.ToArray()));

        var big = new byte[ImageRules.MaxBytes + 1];
        Jpeg.CopyTo(big, 0);
        Assert.Throws<DomainException>(() => ImageRules.DetectContentType(big));
    }

    // ---------- Фото позиции ----------

    [Fact]
    public async Task Загрузка_фото_сохраняет_его_и_ставит_версию()
    {
        var item = await CreateItemAsync();
        Assert.Null(item.ImageVersion);

        var updated = await _service.SetImageAsync(item.Id, Png);

        Assert.Equal(Now.ToUnixTimeMilliseconds(), updated.ImageVersion);
        var image = await _service.GetImageAsync(item.Id);
        Assert.Equal("image/png", image.ContentType);
        Assert.Equal(Png, image.Data);
    }

    [Fact]
    public async Task Повторная_загрузка_заменяет_фото()
    {
        var item = await CreateItemAsync();
        await _service.SetImageAsync(item.Id, Png);
        await _service.SetImageAsync(item.Id, Jpeg);

        Assert.Single(_repo.Images);
        Assert.Equal("image/jpeg", (await _service.GetImageAsync(item.Id)).ContentType);
    }

    [Fact]
    public async Task Удаление_фото()
    {
        var item = await CreateItemAsync();
        await _service.SetImageAsync(item.Id, Png);

        await _service.DeleteImageAsync(item.Id);

        Assert.Empty(_repo.Images);
        Assert.Null((await _service.GetItemAsync(item.Id)).ImageVersion);
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetImageAsync(item.Id));
    }

    [Fact]
    public async Task Фото_для_несуществующей_позиции_NotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.SetImageAsync(999, Png));
    }

    // ---------- Изменение позиции ----------

    [Fact]
    public async Task Изменение_позиции()
    {
        var item = await CreateItemAsync();

        var updated = await _service.UpdateItemAsync(item.Id,
            new UpdateItemRequest("Подшипник 6205", null, 2, 1, 3, "Описание", false));

        Assert.Equal("Подшипник 6205", updated.Name);
        Assert.Equal("м", updated.UnitCode);
        Assert.False(updated.IsActive);
    }

    [Fact]
    public async Task Единицу_нельзя_сменить_после_движений()
    {
        var item = await CreateItemAsync();
        _repo.ItemsWithMovements.Add(item.Id);

        await Assert.ThrowsAsync<DomainException>(() => _service.UpdateItemAsync(item.Id,
            new UpdateItemRequest("Подшипник", null, 2, 0, null, null, true)));

        // А остальное менять можно.
        var updated = await _service.UpdateItemAsync(item.Id,
            new UpdateItemRequest("Подшипник новый", null, 1, 0, null, null, true));
        Assert.Equal("Подшипник новый", updated.Name);
    }

    [Fact]
    public async Task Дубликат_артикула_запрещён()
    {
        await CreateItemAsync("A1");
        await Assert.ThrowsAsync<DomainException>(() => CreateItemAsync("A1"));
    }

    // ---------- Подсказка артикула ----------

    [Fact]
    public async Task Подсказка_артикула_следующий_номер()
    {
        Assert.Equal("ЗЧ-000001", await _service.SuggestSkuAsync());

        await CreateItemAsync("ЗЧ-000007");
        await CreateItemAsync("ЗЧ-000003");
        await CreateItemAsync("ЗЧ-ABC");       // нечисловой хвост не мешает
        await CreateItemAsync("6205-2RS");     // чужие артикулы не учитываются

        Assert.Equal("ЗЧ-000008", await _service.SuggestSkuAsync());
    }

    // ---------- Оборудование ----------

    [Fact]
    public async Task Оборудование_создаётся_деревом()
    {
        var line = await _service.CreateEquipmentAsync(new CreateEquipmentRequest(null, "L1", "Линия розлива 1"));
        var pump = await _service.CreateEquipmentAsync(new CreateEquipmentRequest(line.Id, "L1-P1", "Насос"));

        Assert.Equal(line.Id, pump.ParentId);
        await Assert.ThrowsAsync<DomainException>(() => _service.CreateEquipmentAsync(new CreateEquipmentRequest(null, "L1", "Дубль")));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.CreateEquipmentAsync(new CreateEquipmentRequest(999, "X", "Нет родителя")));
    }
}

/// <summary>Репозиторий справочников в памяти.</summary>
internal class FakeCatalogRepository : ICatalogRepository
{
    public List<Category> Categories { get; } = [];
    public List<UnitOfMeasure> Units { get; } = [];
    public List<Item> Items { get; } = [];
    public List<ItemImage> Images { get; } = [];
    public List<Equipment> Equipment { get; } = [];
    public HashSet<int> ItemsWithMovements { get; } = [];

    private readonly List<object> _added = [];
    private readonly List<object> _removed = [];

    public Task<IReadOnlyList<Category>> ListCategoriesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Category>>(Categories.ToList());

    public Task<bool> CategoryExistsAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Categories.Any(x => x.Id == id));

    public Task<bool> CategoryNameExistsAsync(string name, CancellationToken ct = default) =>
        Task.FromResult(Categories.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)));

    public void AddCategory(Category category) => _added.Add(category);

    public Task<IReadOnlyList<UnitOfMeasure>> ListUnitsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<UnitOfMeasure>>(Units.ToList());

    public Task<bool> UnitExistsAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Units.Any(x => x.Id == id));

    public Task<bool> UnitCodeExistsAsync(string code, CancellationToken ct = default) =>
        Task.FromResult(Units.Any(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase)));

    public void AddUnit(UnitOfMeasure unit) => _added.Add(unit);

    public Task<Item?> GetItemAsync(int id, CancellationToken ct = default)
    {
        var item = Items.FirstOrDefault(x => x.Id == id);
        if (item is not null)
        {
            // Как Include в EF: подставляем навигационные свойства.
            item.Unit = Units.First(u => u.Id == item.UnitId);
            item.Category = Categories.FirstOrDefault(c => c.Id == item.CategoryId);
        }
        return Task.FromResult(item);
    }

    public Task<bool> SkuExistsAsync(string sku, CancellationToken ct = default) =>
        Task.FromResult(Items.Any(x => x.Sku == sku));

    public Task<IReadOnlyList<Item>> ListItemsAsync(string? search, int? categoryId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Item>>(Items.ToList());

    public void AddItem(Item item) => _added.Add(item);

    public Task<IReadOnlyList<string>> ListSkusStartingWithAsync(string prefix, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(Items.Where(x => x.Sku.StartsWith(prefix)).Select(x => x.Sku).ToList());

    public Task<Item?> GetItemForUpdateAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(x => x.Id == id));

    public Task<bool> ItemHasMovementsAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(ItemsWithMovements.Contains(id));

    public Task<ItemImage?> GetImageAsync(int itemId, CancellationToken ct = default) =>
        Task.FromResult(Images.FirstOrDefault(x => x.ItemId == itemId));

    public void AddImage(ItemImage image) => _added.Add(image);

    public void RemoveImage(ItemImage image) => _removed.Add(image);

    public Task<IReadOnlyList<Equipment>> ListEquipmentAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Equipment>>(Equipment.ToList());

    public Task<bool> EquipmentExistsAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Equipment.Any(x => x.Id == id));

    public Task<bool> EquipmentCodeExistsAsync(string code, CancellationToken ct = default) =>
        Task.FromResult(Equipment.Any(x => x.Code == code));

    public void AddEquipment(Equipment equipment) => _added.Add(equipment);

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var entity in _added)
        {
            switch (entity)
            {
                case Category c: c.Id = Categories.Count + 1; Categories.Add(c); break;
                case UnitOfMeasure u: u.Id = Units.Count + 1; Units.Add(u); break;
                case Item i: i.Id = Items.Count + 1; Items.Add(i); break;
                case ItemImage img: Images.Add(img); break;
                case Equipment e: e.Id = Equipment.Count + 1; Equipment.Add(e); break;
            }
        }

        foreach (var entity in _removed)
            if (entity is ItemImage img) Images.Remove(img);

        _added.Clear();
        _removed.Clear();
        return Task.CompletedTask;
    }
}
