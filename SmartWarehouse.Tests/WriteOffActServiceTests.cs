using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Tests;

public class WriteOffActServiceTests
{
    private const int Bearing = 1;  // подшипник, шт
    private const int Belt = 2;     // ремень, шт
    private const int Cell = 10;
    private const int Pump = 5;

    private const int Boss = 1;     // начальник участка, руководителя нет
    private const int Fitter = 2;   // слесарь, подчиняется Boss
    private const int Other = 3;    // слесарь другого участка, руководителя нет
    private const int Keeper = 4;   // кладовщик

    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeStockRepository _stock = new();
    private readonly FakeEmployeeRepository _employees = new();
    private readonly FakeActRepository _acts;
    private readonly StockService _stockService;
    private readonly WriteOffActService _service;

    private static readonly ActActor AsFitter = new(Fitter, "Петров С.", IsWarehouse: false, IsManager: false);
    private static readonly ActActor AsBoss = new(Boss, "Сидоров О.", IsWarehouse: false, IsManager: true);
    private static readonly ActActor AsOther = new(Other, "Козлов А.", IsWarehouse: false, IsManager: false);
    private static readonly ActActor AsKeeper = new(Keeper, "Иванов И.", IsWarehouse: true, IsManager: false);

    public WriteOffActServiceTests()
    {
        var pcs = new UnitOfMeasure { Id = 1, Code = "шт", Name = "штука", IsFractional = false };
        _stock.Items.Add(new Item { Id = Bearing, Sku = "BRG-6205", Name = "Подшипник 6205", UnitId = 1, Unit = pcs });
        _stock.Items.Add(new Item { Id = Belt, Sku = "BLT-A50", Name = "Ремень A50", UnitId = 1, Unit = pcs });
        var warehouse = new Warehouse { Id = 1, Code = "СК1", Name = "Склад" };
        _stock.Locations.Add(new StorageLocation { Id = Cell, WarehouseId = 1, Warehouse = warehouse, Type = LocationType.Cell, Code = "01", FullCode = "СК1-01" });
        _stock.Equipment.Add(new Equipment { Id = Pump, Code = "Н-3", Name = "Насос Н-3" });

        var boss = new Employee { Id = Boss, LastName = "Сидоров", FirstName = "Олег", Position = "Начальник участка" };
        var people = new[]
        {
            boss,
            new Employee { Id = Fitter, LastName = "Петров", FirstName = "Сергей", Position = "Слесарь", ManagerId = Boss, Manager = boss },
            new Employee { Id = Other, LastName = "Козлов", FirstName = "Андрей", Position = "Слесарь" },
            new Employee { Id = Keeper, LastName = "Иванов", FirstName = "Иван", Position = "Кладовщик" }
        };
        _stock.Employees.AddRange(people);
        _employees.Employees.AddRange(people);

        var clock = new FixedTimeProvider(Now);
        _stockService = new StockService(_stock, clock);
        _acts = new FakeActRepository(_stock);
        _service = new WriteOffActService(_acts, _stock, _employees, _stockService, clock);
    }

    /// <summary>Приход на склад и выдача сотруднику.</summary>
    private async Task GiveAsync(int employee, int item, decimal qty)
    {
        await _stockService.PostDocumentAsync(new CreateDocumentRequest(
            MovementType.Receipt, null, null, null, null, [new DocumentLineRequest(item, qty, null, Cell)]));
        await _stockService.PostDocumentAsync(new CreateDocumentRequest(
            MovementType.Issue, null, null, null, null, [new DocumentLineRequest(item, qty, Cell, null)], ReceivedById: employee));
    }

    private decimal Custody(int employee, int item = Bearing) =>
        _stock.EmployeeBalances.FirstOrDefault(b => b.EmployeeId == employee && b.ItemId == item)?.Quantity ?? 0;

    private static SaveActRequest Request(int employee, params ActLineRequest[] lines) =>
        new(employee, null, null, "Наряд 15", null, lines);

    private static ActLineRequest Line(decimal qty, int item = Bearing, int? equipment = null, string? usage = "замена подшипника") =>
        new(item, qty, equipment, usage);

    // ---------- Черновик ----------

    [Fact]
    public async Task Сотрудник_составляет_черновик_ничего_не_списывается()
    {
        await GiveAsync(Fitter, Bearing, 5);

        var act = await _service.CreateAsync(Request(Fitter, Line(2, equipment: Pump), Line(1, usage: "на стенд")), AsFitter);

        Assert.Equal("АС-000001", act.Number);
        Assert.Equal(ActStatus.Draft, act.Status);
        Assert.Equal("Сидоров О.", act.ApproverName);
        Assert.Equal(2, act.Lines.Count);
        Assert.Equal("Н-3 — Насос Н-3", act.Lines[0].Equipment);
        Assert.True(act.Can.Edit);
        Assert.False(act.Can.Decide);
        Assert.Equal(5, Custody(Fitter));
    }

    [Fact]
    public async Task В_каждой_строке_нужно_указать_куда_использовано()
    {
        await GiveAsync(Fitter, Bearing, 5);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.CreateAsync(Request(Fitter, Line(1), Line(1, usage: "  ")), AsFitter));

        Assert.Contains("Строка 2", ex.Message);
        Assert.Contains("куда использовано", ex.Message);
    }

    [Fact]
    public async Task Нельзя_включить_больше_чем_числится_по_всем_строкам()
    {
        await GiveAsync(Fitter, Bearing, 3);

        await Assert.ThrowsAsync<InsufficientStockException>(() =>
            _service.CreateAsync(Request(Fitter, Line(2), Line(2)), AsFitter));
    }

    [Fact]
    public async Task Чужой_акт_составить_нельзя_а_кладовщику_можно()
    {
        await GiveAsync(Fitter, Bearing, 3);

        await Assert.ThrowsAsync<ForbiddenException>(() => _service.CreateAsync(Request(Fitter, Line(1)), AsOther));

        var act = await _service.CreateAsync(Request(Fitter, Line(1)), AsKeeper);
        Assert.Equal(Fitter, act.EmployeeId);
        Assert.Equal("Иванов И.", act.CreatedBy);
    }

    [Fact]
    public async Task Период_и_даты_строк_проверяются()
    {
        await GiveAsync(Fitter, Bearing, 3);
        var from = new DateOnly(2026, 10, 1);
        var to = new DateOnly(2026, 10, 20);

        await Assert.ThrowsAsync<DomainException>(() => _service.CreateAsync(
            new SaveActRequest(Fitter, to, from, null, null, [Line(1)]), AsFitter));
        var outside = await Assert.ThrowsAsync<DomainException>(() => _service.CreateAsync(
            new SaveActRequest(Fitter, from, to, null, null, [Line(1) with { UsedOn = new DateOnly(2026, 10, 25) }]), AsFitter));
        Assert.Contains("вне периода", outside.Message);

        var act = await _service.CreateAsync(
            new SaveActRequest(Fitter, from, to, null, null, [Line(1) with { UsedOn = new DateOnly(2026, 10, 5) }]), AsFitter);
        Assert.Equal(from, act.PeriodFrom);
        Assert.Equal(new DateOnly(2026, 10, 5), act.Lines[0].UsedOn);
    }

    // ---------- Утверждение ----------

    [Fact]
    public async Task Отправленный_акт_резервирует_и_утверждается_руководителем()
    {
        await GiveAsync(Fitter, Bearing, 5);
        var draft = await _service.CreateAsync(Request(Fitter, Line(3, equipment: Pump)), AsFitter);

        var pending = await _service.SubmitAsync(draft.Id, AsFitter);
        Assert.Equal(ActStatus.Pending, pending.Status);
        Assert.False(pending.Can.Edit);
        Assert.True(pending.Can.Recall);
        Assert.Equal(5, Custody(Fitter)); // пока только резерв
        Assert.Equal(1, await _service.CountToApproveAsync(AsBoss));
        Assert.Single(await _service.ListAsync(ActScope.ToApprove, null, null, AsBoss));

        // Резерв: второй акт на те же позиции не пройдёт.
        await Assert.ThrowsAsync<InsufficientStockException>(() => _service.CreateAsync(Request(Fitter, Line(3)), AsFitter));

        // Утвердить может только руководитель.
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.ApproveAsync(draft.Id, AsFitter));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.ApproveAsync(draft.Id, AsKeeper));

        var approved = await _service.ApproveAsync(draft.Id, AsBoss);

        Assert.Equal(ActStatus.Approved, approved.Status);
        Assert.Equal("Сидоров О.", approved.DecidedBy);
        Assert.Equal("Начальник участка", approved.DecidedByPosition);
        Assert.Equal(2, Custody(Fitter));
        var doc = _stock.Documents.Last();
        Assert.Equal(MovementType.WriteOff, doc.Type);
        Assert.Equal(approved.DocumentId, doc.Id);
        Assert.Equal(Pump, doc.EquipmentId);
        Assert.Equal("АС-000001", doc.ExternalNumber);
        Assert.Equal(Boss, doc.PerformedById);
        Assert.Equal(Fitter, doc.ReceivedById);
        Assert.Equal(0, await _service.CountToApproveAsync(AsBoss));
    }

    [Fact]
    public async Task Возврат_на_доработку_с_замечанием_снимает_резерв()
    {
        await GiveAsync(Fitter, Bearing, 5);
        var act = await _service.CreateAsync(Request(Fitter, Line(5)), AsFitter);
        await _service.SubmitAsync(act.Id, AsFitter);

        await Assert.ThrowsAsync<DomainException>(() => _service.ReturnAsync(act.Id, "  ", AsBoss));
        var returned = await _service.ReturnAsync(act.Id, "Укажите оборудование", AsBoss);

        Assert.Equal(ActStatus.Returned, returned.Status);
        Assert.Equal("Укажите оборудование", returned.ReturnRemark);
        Assert.False(returned.Can.Edit);                                  // руководитель сам не правит
        Assert.True((await _service.GetAsync(act.Id, AsFitter)).Can.Edit); // а сотрудник — может
        Assert.Equal(0, await _stock.GetReservedAsync(Bearing, Fitter, null));

        // Исправил и отправил снова.
        var fixedAct = await _service.UpdateAsync(act.Id, Request(Fitter, Line(5, equipment: Pump)), AsFitter);
        Assert.Equal(ActStatus.Draft, fixedAct.Status);
        await _service.SubmitAsync(act.Id, AsFitter);
        var approved = await _service.ApproveAsync(act.Id, AsBoss);
        Assert.Null(approved.ReturnRemark);
        Assert.Equal(0, Custody(Fitter));
    }

    [Fact]
    public async Task Без_руководителя_сотрудник_утверждает_сам_сразу_при_отправке()
    {
        await GiveAsync(Other, Bearing, 2);
        var act = await _service.CreateAsync(Request(Other, Line(2)), AsOther);
        Assert.Equal(Other, act.ApproverId);

        var approved = await _service.SubmitAsync(act.Id, AsOther);

        Assert.Equal(ActStatus.Approved, approved.Status);
        Assert.Equal("Козлов А.", approved.DecidedBy);
        Assert.Equal(0, Custody(Other));
    }

    [Fact]
    public async Task Отзыв_с_утверждения_возвращает_в_черновик()
    {
        await GiveAsync(Fitter, Bearing, 2);
        var act = await _service.CreateAsync(Request(Fitter, Line(2)), AsFitter);
        await _service.SubmitAsync(act.Id, AsFitter);

        await Assert.ThrowsAsync<DomainException>(() => _service.UpdateAsync(act.Id, Request(Fitter, Line(1)), AsFitter));
        var recalled = await _service.RecallAsync(act.Id, AsFitter);

        Assert.Equal(ActStatus.Draft, recalled.Status);
        Assert.Equal(0, await _service.CountToApproveAsync(AsBoss));
    }

    [Fact]
    public async Task Утверждённый_акт_нельзя_изменить_или_удалить()
    {
        await GiveAsync(Other, Bearing, 2);
        var act = await _service.CreateAsync(Request(Other, Line(1)), AsOther);
        await _service.SubmitAsync(act.Id, AsOther);

        await Assert.ThrowsAsync<DomainException>(() => _service.UpdateAsync(act.Id, Request(Other, Line(1)), AsOther));
        await Assert.ThrowsAsync<DomainException>(() => _service.DeleteAsync(act.Id, AsOther));
        await Assert.ThrowsAsync<DomainException>(() => _service.ApproveAsync(act.Id, AsOther));
    }

    [Fact]
    public async Task Черновик_можно_удалить()
    {
        await GiveAsync(Fitter, Bearing, 2);
        var act = await _service.CreateAsync(Request(Fitter, Line(1)), AsFitter);

        await _service.DeleteAsync(act.Id, AsFitter);

        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetAsync(act.Id, AsFitter));
    }

    // ---------- Видимость ----------

    [Fact]
    public async Task Видимость_свои_подчинённые_все()
    {
        await GiveAsync(Fitter, Bearing, 2);
        await GiveAsync(Other, Belt, 2);
        var fitterAct = await _service.CreateAsync(Request(Fitter, Line(1)), AsFitter);
        await _service.CreateAsync(Request(Other, Line(1, item: Belt)), AsOther);

        Assert.Single(await _service.ListAsync(ActScope.Visible, null, null, AsFitter));
        Assert.Single(await _service.ListAsync(ActScope.Visible, null, null, AsBoss));   // только подчинённый
        Assert.Equal(2, (await _service.ListAsync(ActScope.Visible, null, null, AsKeeper)).Count);

        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetAsync(fitterAct.Id, AsOther));
        Assert.Equal(fitterAct.Id, (await _service.GetAsync(fitterAct.Id, AsBoss)).Id);
    }

    // ---------- Давность подотчёта ----------

    [Fact]
    public void Давность_считается_по_самой_ранней_невозвращённой_выдаче()
    {
        var d1 = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var d2 = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var d3 = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
        IssueRecord[] issues = [new(Fitter, Bearing, d1, 5), new(Fitter, Bearing, d2, 3), new(Fitter, Bearing, d3, 2)];

        Assert.Equal(d3, EmployeeService.HeldSince(2, issues));   // осталось только последнее
        Assert.Equal(d2, EmployeeService.HeldSince(4, issues));   // последнее + часть предыдущего
        Assert.Equal(d1, EmployeeService.HeldSince(10, issues));
        Assert.Null(EmployeeService.HeldSince(1, []));
    }
}

/// <summary>Акты в памяти; сохранение сохраняет и складские документы (как одна транзакция в EF).</summary>
internal class FakeActRepository(FakeStockRepository stock) : IWriteOffActRepository
{
    private readonly List<WriteOffAct> _pending = [];
    private int _nextId = 1;

    public Task<WriteOffAct?> GetAsync(int id, CancellationToken ct = default)
    {
        var act = stock.Acts.FirstOrDefault(a => a.Id == id);
        if (act is not null)
            Populate(act);
        return Task.FromResult(act);
    }

    public Task<IReadOnlyList<ActHeader>> ListAsync(ActFilter filter, CancellationToken ct = default)
    {
        foreach (var act in stock.Acts)
            Populate(act);

        var query = stock.Acts.AsEnumerable();
        if (filter.EmployeeIds is not null)
            query = query.Where(a => filter.EmployeeIds.Contains(a.EmployeeId));
        if (filter.ApproverId is int approver)
            query = query.Where(a => IsApprover(a, approver));
        if (filter.Status is ActStatus status)
            query = query.Where(a => a.Status == status);
        if (filter.EmployeeId is int employeeId)
            query = query.Where(a => a.EmployeeId == employeeId);

        return Task.FromResult<IReadOnlyList<ActHeader>>(
            query.OrderByDescending(a => a.Id).Take(filter.Take).Select(a => new ActHeader(a, a.Lines.Count)).ToList());
    }

    public Task<int> CountToApproveAsync(int approverId, CancellationToken ct = default)
    {
        foreach (var act in stock.Acts)
            Populate(act);
        return Task.FromResult(stock.Acts.Count(a => a.Status == ActStatus.Pending && IsApprover(a, approverId)));
    }

    public void Add(WriteOffAct act) => _pending.Add(act);

    public void Remove(WriteOffAct act) => stock.Acts.Remove(act);

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await stock.SaveChangesAsync(ct);
        foreach (var act in _pending)
        {
            act.Id = _nextId++;
            stock.Acts.Add(act);
        }
        _pending.Clear();
        foreach (var act in stock.Acts.Where(a => a.Document is not null))
            act.DocumentId = act.Document!.Id;
    }

    private static bool IsApprover(WriteOffAct act, int approverId) =>
        act.Employee.ManagerId == approverId || (act.Employee.ManagerId is null && act.EmployeeId == approverId);

    /// <summary>Навигационные свойства, как после Include в EF.</summary>
    private void Populate(WriteOffAct act)
    {
        act.Employee = stock.Employees.First(e => e.Id == act.EmployeeId);
        foreach (var line in act.Lines)
        {
            line.Item = stock.Items.First(i => i.Id == line.ItemId);
            line.Equipment = stock.Equipment.FirstOrDefault(e => e.Id == line.EquipmentId);
        }
    }
}
