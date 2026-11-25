using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;
using SmartWarehouse.Web.Services;

namespace SmartWarehouse.Tests;

public class ItemNoteTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    private static readonly NoteActor Ivanov = new(10, "Иванов П.А.", IsStaff: false);
    private static readonly NoteActor Petrov = new(11, "Петров С.С.", IsStaff: false);
    private static readonly NoteActor Storekeeper = new(12, "Дорофей С.М.", IsStaff: true);

    private readonly FakeItemNoteRepository _repo = new();
    private readonly ItemNoteService _service;

    public ItemNoteTests()
    {
        _repo.ItemIds.Add(1);
        _service = new ItemNoteService(_repo, new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task Заметку_пишет_любой_с_учётной_записью_и_подписывается_автор()
    {
        var note = await _service.AddAsync(1, new SaveItemNoteRequest(NoteKind.Analog, "  Подходит 6205-2Z (FAG)  "), Ivanov);

        Assert.Equal("Подходит 6205-2Z (FAG)", note.Text);
        Assert.Equal("Иванов П.А.", note.AuthorName);
        Assert.True(note.Mine && note.CanEdit && note.CanDelete);
        Assert.Equal(Now, note.CreatedAt);
        Assert.Null(note.UpdatedAt);
    }

    [Fact]
    public async Task Пустая_длинная_и_к_несуществующей_позиции_не_принимается()
    {
        await Assert.ThrowsAsync<DomainException>(() => _service.AddAsync(1, new SaveItemNoteRequest(NoteKind.Tip, "   "), Ivanov));
        await Assert.ThrowsAsync<DomainException>(() =>
            _service.AddAsync(1, new SaveItemNoteRequest(NoteKind.Tip, new string('а', ItemNoteService.MaxLength + 1)), Ivanov));
        await Assert.ThrowsAsync<DomainException>(() => _service.AddAsync(1, new SaveItemNoteRequest((NoteKind)99, "текст"), Ivanov));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.AddAsync(2, new SaveItemNoteRequest(NoteKind.Tip, "текст"), Ivanov));
    }

    [Fact]
    public async Task Менять_может_только_автор_удалять_автор_кладовщик_администратор()
    {
        var note = await _service.AddAsync(1, new SaveItemNoteRequest(NoteKind.Usage, "Насос Г2-ОПБ"), Ivanov);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.UpdateAsync(note.Id, new SaveItemNoteRequest(NoteKind.Usage, "Чужая правка"), Storekeeper));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.DeleteAsync(note.Id, Petrov));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.PinAsync(note.Id, true, Petrov));

        var asOther = (await _service.ListAsync(1, Petrov)).Single();
        Assert.False(asOther.CanEdit || asOther.CanDelete || asOther.CanPin);
        var asStorekeeper = (await _service.ListAsync(1, Storekeeper)).Single();
        Assert.False(asStorekeeper.CanEdit);
        Assert.True(asStorekeeper.CanDelete && asStorekeeper.CanPin);

        var edited = await _service.UpdateAsync(note.Id, new SaveItemNoteRequest(NoteKind.Usage, "Насос Г2-ОПБ, линия 2"), Ivanov);
        Assert.Equal(Now, edited.UpdatedAt);

        Assert.True((await _service.PinAsync(note.Id, true, Storekeeper)).Pinned);
        await _service.DeleteAsync(note.Id, Storekeeper);
        Assert.Empty(await _service.ListAsync(1, Ivanov));
    }

    [Fact]
    public void Отрывок_вокруг_найденного_слова()
    {
        var text = "Подходит 6205-2Z (FAG, NSK) — проверено на гомогенизаторе А1-ОГМ. Закрытый 2Z хуже держит мойку, на линии розлива лучше 2RS.";

        var snippet = ItemNoteService.Snippet(text, "ГОМОГЕНИЗАТОР", around: 20);
        Assert.Contains("гомогенизаторе", snippet);
        Assert.StartsWith("…", snippet);
        Assert.EndsWith("…", snippet);
        Assert.True(snippet.Length < text.Length);

        Assert.Equal("Коротко", ItemNoteService.Snippet("Коротко", "нет такого"));
    }

    [Fact]
    public void Слова_запроса()
    {
        Assert.Equal(["подшипник", "6205-2RS"], SearchWords.Split("  подшипник,  6205-2RS  ПОДШИПНИК "));
        Assert.Empty(SearchWords.Split("   "));
        Assert.Equal(SearchWords.MaxWords, SearchWords.Split("а б в г д е ж з и к л").Count);

        Assert.True(SearchWords.Matches("6205 подшипник", "Подшипник шариковый 6205-2RS", "BRG-1"));
        Assert.True(SearchWords.Matches("brg шариковый", "Подшипник шариковый 6205-2RS", "BRG-1"));
        Assert.False(SearchWords.Matches("подшипник 6206", "Подшипник шариковый 6205-2RS", "BRG-1"));
        Assert.True(SearchWords.Matches(null, "что угодно"));

        // «%», «_» и «\» ищутся как обычные символы.
        Assert.Equal(@"%50\%\_\\%", SearchWords.LikePattern(@"50%_\"));
    }

    [Fact]
    public void Подсветка_нескольких_слов()
    {
        var parts = Ui.Highlight("Подшипник шариковый 6205-2RS", "6205 подшипник").ToList();
        Assert.Equal([("Подшипник", true), (" шариковый ", false), ("6205", true), ("-2RS", false)], parts);
    }

    [Fact]
    public async Task Слова_из_названия_и_из_заметки_вместе()
    {
        var catalog = new FakeCatalogRepository();
        var unit = new UnitOfMeasure { Id = 1, Code = "шт", Name = "штука" };
        catalog.Units.Add(unit);
        catalog.Items.Add(new Item { Id = 1, Sku = "BRG-1", Name = "Подшипник 6205-2RS", UnitId = 1, Unit = unit, IsActive = true });
        await _service.AddAsync(1, new SaveItemNoteRequest(NoteKind.Usage, "Стоит на гомогенизаторе А1-ОГМ"), Ivanov);

        var service = new CatalogService(catalog, new FixedTimeProvider(Now), _repo);
        var found = Assert.Single(await service.ListItemsAsync("подшипник гомогенизатор", null));

        // «подшипник» — в названии, «гомогенизатор» — только в заметке: показываем отрывок заметки.
        Assert.NotNull(found.NoteHit);
        Assert.Contains("гомогенизаторе", found.NoteHit!.Snippet);
        Assert.Null(Assert.Single(await service.ListItemsAsync("подшипник 6205", null)).NoteHit);
    }

    [Fact]
    public void Подсветка_найденного_без_учёта_регистра()
    {
        var parts = Ui.Highlight("на Гомогенизаторе и гомогенизатор", "гомогенизатор").ToList();
        Assert.Equal(
            [("на ", false), ("Гомогенизатор", true), ("е и ", false), ("гомогенизатор", true)],
            parts);
        Assert.Equal([("текст", false)], Ui.Highlight("текст", null).ToList());
    }

    [Fact]
    public async Task Поиск_номенклатуры_находит_по_заметке_и_ставит_такие_ниже()
    {
        var catalog = new FakeCatalogRepository();
        var unit = new UnitOfMeasure { Id = 1, Code = "шт", Name = "штука" };
        catalog.Units.Add(unit);
        // Репозиторий уже отобрал позиции по названию или заметке (в БД — ILIKE), отдаёт по наименованию.
        catalog.Items.Add(new Item { Id = 1, Sku = "BRG-1", Name = "Подшипник 6205-2RS", UnitId = 1, Unit = unit, IsActive = true });
        catalog.Items.Add(new Item { Id = 2, Sku = "VLV-1", Name = "Клапан гомогенизатора", UnitId = 1, Unit = unit, IsActive = true });
        _repo.ItemIds.Add(2);
        await _service.AddAsync(1, new SaveItemNoteRequest(NoteKind.Analog, "Проверено на гомогенизаторе А1-ОГМ"), Ivanov);
        await _service.AddAsync(1, new SaveItemNoteRequest(NoteKind.Tip, "Греть до 80 °C"), Ivanov);

        var service = new CatalogService(catalog, new FixedTimeProvider(Now), _repo);
        var found = await service.ListItemsAsync("гомогенизатор", null);

        Assert.Equal([2, 1], found.Select(i => i.Id));
        Assert.Null(found[0].NoteHit);
        Assert.Equal(NoteKind.Analog, found[1].NoteHit!.Kind);
        Assert.Contains("гомогенизаторе", found[1].NoteHit!.Snippet);
        Assert.Equal(2, found[1].NoteCount);
        Assert.Equal(0, found[0].NoteCount);
    }
}

internal class FakeItemNoteRepository : IItemNoteRepository
{
    public HashSet<int> ItemIds { get; } = [];
    public List<ItemNote> Notes { get; } = [];
    private int _nextId = 1;

    public Task<bool> ItemExistsAsync(int itemId, CancellationToken ct = default) => Task.FromResult(ItemIds.Contains(itemId));

    public Task<IReadOnlyList<ItemNote>> ListAsync(int itemId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ItemNote>>(Notes.Where(n => n.ItemId == itemId)
            .OrderByDescending(n => n.Pinned).ThenByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).ToList());

    public Task<ItemNote?> GetAsync(int id, CancellationToken ct = default) => Task.FromResult(Notes.FirstOrDefault(n => n.Id == id));

    public Task<IReadOnlyDictionary<int, int>> CountByItemAsync(IReadOnlyCollection<int> itemIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<int, int>>(Notes.Where(n => itemIds.Contains(n.ItemId))
            .GroupBy(n => n.ItemId).ToDictionary(g => g.Key, g => g.Count()));

    public Task<IReadOnlyList<ItemNote>> FindAsync(IReadOnlyCollection<int> itemIds, IReadOnlyList<string> words, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ItemNote>>(Notes
            .Where(n => itemIds.Contains(n.ItemId) && SearchWords.FirstMatch(n.Text, words).Index >= 0)
            .OrderByDescending(n => n.Pinned).ThenByDescending(n => n.CreatedAt).ToList());

    public void Add(ItemNote note)
    {
        note.Id = _nextId++;
        Notes.Add(note);
    }

    public void Remove(ItemNote note) => Notes.Remove(note);

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}
