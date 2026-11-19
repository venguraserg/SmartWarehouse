using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Tests;

public class IssueRequestServiceTests
{
    private const int Bearing = 1;   // шт
    private const int Cable = 2;     // м, дробное
    private const int Storekeeper = 1;
    private const int Worker = 2;
    private const int Other = 3;
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeStockRepository _stock = new();
    private readonly FakeIssueRequestRepository _repo;
    private readonly IssueRequestService _service;

    // Кто выдаёт (кладовщик, старший смены) — видит все; сотрудник — свои.
    private static readonly IssueRequestActor Keeper = new(Storekeeper, "Иванов И.П.", CanIssue: true, Visible: null);
    private static readonly IssueRequestActor Me = new(Worker, "Петров С.", CanIssue: false, Visible: [Worker]);
    private static readonly IssueRequestActor Stranger = new(Other, "Сидоров О.", CanIssue: false, Visible: [Other]);

    public IssueRequestServiceTests()
    {
        var pcs = new UnitOfMeasure { Id = 1, Code = "шт", Name = "штука" };
        var meter = new UnitOfMeasure { Id = 2, Code = "м", Name = "метр", IsFractional = true };
        _stock.Items.Add(new Item { Id = Bearing, Sku = "BRG-6205", Name = "Подшипник 6205", UnitId = 1, Unit = pcs });
        _stock.Items.Add(new Item { Id = Cable, Sku = "CBL-3X1.5", Name = "Кабель ВВГ 3х1,5", UnitId = 2, Unit = meter });
        _stock.Employees.Add(new Employee { Id = Storekeeper, LastName = "Иванов", FirstName = "Иван", MiddleName = "Петрович" });
        _stock.Employees.Add(new Employee { Id = Worker, LastName = "Петров", FirstName = "Сергей", Position = "Слесарь" });
        _stock.Employees.Add(new Employee { Id = Other, LastName = "Сидоров", FirstName = "Олег" });

        var cellA = new StorageLocation { Id = 10, FullCode = "1.A1.1.1", Name = "Ящик 1" };
        var cellB = new StorageLocation { Id = 20, FullCode = "1.A1.1.2" };
        _stock.Balances.Add(new StockBalance { ItemId = Bearing, LocationId = 10, Location = cellA, BatchId = 1, Quantity = 3 });
        _stock.Balances.Add(new StockBalance { ItemId = Bearing, LocationId = 20, Location = cellB, BatchId = 2, Quantity = 4 });
        _stock.Balances.Add(new StockBalance { ItemId = Cable, LocationId = 20, Location = cellB, BatchId = 3, Quantity = 12.5m });

        _repo = new FakeIssueRequestRepository(_stock);
        _service = new IssueRequestService(_repo, _stock, new FixedTimeProvider(Now));
    }

    private Task<IssueRequestDto> Create(IssueRequestActor actor, int? employeeId = null, params (int Item, decimal Qty)[] lines) =>
        _service.CreateAsync(new CreateIssueRequestRequest(employeeId, "к ремонту насоса",
            lines.Select(l => new IssueRequestLineRequest(l.Item, l.Qty)).ToList()), actor);

    [Fact]
    public async Task Сотрудник_запрашивает_из_остатков_на_себя()
    {
        var request = await Create(Me, null, (Bearing, 5), (Cable, 2.5m), (Bearing, 1));

        Assert.Equal("ЗВ-000001", request.Number);
        Assert.Equal((IssueRequestStatus.Open, Worker), (request.Status, request.EmployeeId));
        // Одинаковые позиции складываются.
        Assert.Equal([(Bearing, 6m), (Cable, 2.5m)], request.Lines.Select(l => (l.ItemId, l.Quantity)));
        Assert.Equal(7, request.Lines[0].OnHand);
        Assert.Empty(request.Lines[0].Places);            // где лежит — только тем, кто выдаёт
        Assert.Equal(new IssueRequestPermissions(Issue: false, Reject: false, Cancel: true), request.Can);
    }

    [Fact]
    public async Task Запросить_больше_остатка_или_на_другого_нельзя()
    {
        var tooMuch = await Assert.ThrowsAsync<DomainException>(() => Create(Me, null, (Bearing, 8)));
        Assert.Contains("только 7", tooMuch.Message);
        await Assert.ThrowsAsync<DomainException>(() => Create(Me, null, (Bearing, 1.5m)));   // штуки — целые
        await Assert.ThrowsAsync<ForbiddenException>(() => Create(Me, Other, (Bearing, 1)));

        // Кладовщик может оформить запрос со слов сотрудника.
        var forOther = await Create(Keeper, Other, (Bearing, 1));
        Assert.Equal(Other, forOther.EmployeeId);
    }

    [Fact]
    public async Task Кладовщик_видит_где_лежит_и_закрывает_запрос_выдачей()
    {
        var created = await Create(Me, null, (Bearing, 5));
        var seen = await _service.GetAsync(created.Id, Keeper);
        Assert.Equal(["1.A1.1.1:3", "1.A1.1.2:4"], seen.Lines[0].Places.Select(p => $"{p.LocationCode}:{p.Quantity:0}"));
        Assert.True(seen.Can.Issue);

        // Выдача — только тому, кто просил.
        await Assert.ThrowsAsync<DomainException>(() => _service.EnsureCanIssueAsync(created.Id, Other, Keeper));
        await _service.EnsureCanIssueAsync(created.Id, Worker, Keeper);
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.EnsureCanIssueAsync(created.Id, Worker, Me));

        // Выдали меньше, чем просили (что было) — запрос всё равно закрыт.
        var document = new StockDocument { Id = 40, Type = MovementType.Issue };
        document.Lines.Add(new StockMovement { ItemId = Bearing, Quantity = 4 });
        _stock.Documents.Add(document);
        await _service.MarkIssuedAsync(created.Id, 40, Keeper);

        var issued = await _service.GetAsync(created.Id, Me);
        Assert.Equal(IssueRequestStatus.Issued, issued.Status);
        Assert.Equal((40L, 4m), (issued.DocumentId!.Value, issued.Lines[0].Issued!.Value));
        Assert.Equal("Иванов И.П.", issued.ClosedBy);
        await Assert.ThrowsAsync<DomainException>(() => _service.EnsureCanIssueAsync(created.Id, Worker, Keeper));
    }

    [Fact]
    public async Task Отменяет_сам_сотрудник_отклоняет_кладовщик_с_причиной()
    {
        var first = await Create(Me, null, (Bearing, 1));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.CancelAsync(first.Id, Keeper));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetAsync(first.Id, Stranger));
        var cancelled = await _service.CancelAsync(first.Id, Me);
        Assert.Equal(IssueRequestStatus.Cancelled, cancelled.Status);

        var second = await Create(Me, null, (Cable, 3));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.RejectAsync(second.Id, "нет", Me));
        await Assert.ThrowsAsync<DomainException>(() => _service.RejectAsync(second.Id, " ", Keeper));
        var rejected = await _service.RejectAsync(second.Id, "кабель зарезервирован под линию 2", Keeper);
        Assert.Equal((IssueRequestStatus.Rejected, "кабель зарезервирован под линию 2"), (rejected.Status, rejected.CloseNote));

        Assert.Equal(0, await _service.CountOpenAsync(Keeper));
        Assert.Equal(2, (await _service.ListAsync(null, Me)).Count);
        Assert.Empty(await _service.ListAsync(null, Stranger));
    }
}

/// <summary>Запросы в памяти: при сохранении — номер и навигация (сотрудник, позиция, документ) из склада.</summary>
internal class FakeIssueRequestRepository(FakeStockRepository stock) : IIssueRequestRepository
{
    public List<IssueRequest> Requests { get; } = [];

    public void Add(IssueRequest request) => Requests.Add(request);

    public Task<IssueRequest?> GetAsync(int id, CancellationToken ct = default)
    {
        var request = Requests.FirstOrDefault(r => r.Id == id);
        if (request is not null)
            Populate(request);
        return Task.FromResult(request);
    }

    public Task<IReadOnlyList<IssueRequest>> ListAsync(IReadOnlyCollection<int>? employeeIds, IssueRequestStatus? status, int take, CancellationToken ct = default)
    {
        Requests.ForEach(Populate);
        return Task.FromResult<IReadOnlyList<IssueRequest>>(Requests
            .Where(r => employeeIds is null || employeeIds.Contains(r.EmployeeId))
            .Where(r => status is null || r.Status == status)
            .Take(take)
            .ToList());
    }

    public Task<int> CountOpenAsync(CancellationToken ct = default) =>
        Task.FromResult(Requests.Count(r => r.Status == IssueRequestStatus.Open));

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var r in Requests.Where(r => r.Id == 0))
            r.Id = Requests.Max(x => x.Id) + 1;
        Requests.ForEach(Populate);
        return Task.CompletedTask;
    }

    private void Populate(IssueRequest r)
    {
        r.Employee = stock.Employees.First(e => e.Id == r.EmployeeId);
        foreach (var l in r.Lines)
            l.Item = stock.Items.First(i => i.Id == l.ItemId);
        r.Document = r.DocumentId is long docId ? stock.Documents.FirstOrDefault(d => d.Id == docId) : null;
    }
}
