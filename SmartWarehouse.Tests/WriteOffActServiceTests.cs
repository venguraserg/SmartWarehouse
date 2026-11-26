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
        _stockService = new StockService(_stock, clock, new FakeItemNoteRepository());
        _acts = new FakeActRepository(_stock);
        _service = new WriteOffActService(_acts, _stock, _employees, _stockService, clock);
    }

    /// <summary>Приход на склад (партия основного МОЛ или указанного <paramref name="mol"/>) и выдача сотруднику «в работу».</summary>
    private async Task GiveAsync(int employee, int item, decimal qty, CustodyKind kind = CustodyKind.Work, bool offBook = false, int? mol = null)
    {
        await _stockService.PostDocumentAsync(new CreateDocumentRequest(
            MovementType.Receipt, offBook ? null : "ТТН 1", null, null, null,
            [new DocumentLineRequest(item, qty, null, Cell, UnitPrice: offBook ? null : 10)], OffBook: offBook, MolId: mol));
        await _stockService.PostDocumentAsync(new CreateDocumentRequest(
            MovementType.Issue, null, null, null, null, [new DocumentLineRequest(item, qty, Cell, null)], ReceivedById: employee,
            Custody: kind));
    }

    private decimal Custody(int employee, int item = Bearing) =>
        _stock.EmployeeBalances.Where(b => b.EmployeeId == employee && b.ItemId == item).Sum(b => b.Quantity);

    /// <summary>Отчёт об использовании (акты по подотчёту 1С больше не оформляются).</summary>
    private static SaveActRequest Request(int employee, params ActLineRequest[] lines) =>
        new(employee, null, null, "Наряд 15", null, lines, ActKind.UsageReport);

    private static ActLineRequest Line(decimal qty, int item = Bearing, int? equipment = null, string? usage = "замена подшипника") =>
        new(item, qty, equipment, usage);

    // ---------- Черновик ----------

    [Fact]
    public async Task Сотрудник_составляет_черновик_ничего_не_списывается()
    {
        await GiveAsync(Fitter, Bearing, 5);

        var act = await _service.CreateAsync(Request(Fitter, Line(2, equipment: Pump), Line(1, usage: "на стенд")), AsFitter);

        Assert.Equal("ОИ-000001", act.Number);
        Assert.Equal(ActStatus.Draft, act.Status);
        Assert.Equal("кладовщик", act.ApproverName);
        Assert.Equal(2, act.Lines.Count);
        Assert.Equal("Н-3 — Насос Н-3", act.Lines[0].Equipment);
        Assert.True(act.Can.Edit);
        Assert.False(act.Can.Decide);
        Assert.Equal(5, Custody(Fitter));
    }

    [Fact]
    public async Task Акт_по_подотчёту_1С_больше_не_оформляется()
    {
        await GiveAsync(Fitter, Bearing, 5);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.CreateAsync(new SaveActRequest(Fitter, null, null, null, null, [Line(1)]), AsFitter));
        Assert.Contains("сводным актом", ex.Message);
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
            new SaveActRequest(Fitter, to, from, null, null, [Line(1)], ActKind.UsageReport), AsFitter));
        var outside = await Assert.ThrowsAsync<DomainException>(() => _service.CreateAsync(
            new SaveActRequest(Fitter, from, to, null, null, [Line(1) with { UsedOn = new DateOnly(2026, 10, 25) }], ActKind.UsageReport), AsFitter));
        Assert.Contains("вне периода", outside.Message);

        var act = await _service.CreateAsync(
            new SaveActRequest(Fitter, from, to, null, null, [Line(1) with { UsedOn = new DateOnly(2026, 10, 5) }], ActKind.UsageReport), AsFitter);
        Assert.Equal(from, act.PeriodFrom);
        Assert.Equal(new DateOnly(2026, 10, 5), act.Lines[0].UsedOn);
    }

    // ---------- Отправка, отзыв, удаление ----------

    [Fact]
    public async Task Отзыв_с_подтверждения_возвращает_в_черновик()
    {
        await GiveAsync(Fitter, Bearing, 2);
        var act = await _service.CreateAsync(Request(Fitter, Line(2)), AsFitter);
        await _service.SubmitAsync(act.Id, AsFitter);

        await Assert.ThrowsAsync<DomainException>(() => _service.UpdateAsync(act.Id, Request(Fitter, Line(1)), AsFitter));
        var recalled = await _service.RecallAsync(act.Id, AsFitter);

        Assert.Equal(ActStatus.Draft, recalled.Status);
        Assert.Equal(0, await _service.CountToApproveAsync(AsKeeper));
    }

    [Fact]
    public async Task Подтверждённый_отчёт_нельзя_изменить_или_удалить()
    {
        await GiveAsync(Other, Bearing, 2);
        var act = await _service.CreateAsync(Request(Other, Line(1)), AsKeeper);
        await _service.SubmitAsync(act.Id, AsKeeper);   // внесено кладовщиком — подтверждено сразу

        await Assert.ThrowsAsync<DomainException>(() => _service.UpdateAsync(act.Id, Request(Other, Line(1)), AsKeeper));
        await Assert.ThrowsAsync<DomainException>(() => _service.DeleteAsync(act.Id, AsKeeper));
        await Assert.ThrowsAsync<DomainException>(() => _service.ApproveAsync(act.Id, AsKeeper));
    }

    [Fact]
    public async Task Черновик_можно_удалить()
    {
        await GiveAsync(Fitter, Bearing, 2);
        var act = await _service.CreateAsync(Request(Fitter, Line(1)), AsFitter);

        await _service.DeleteAsync(act.Id, AsFitter);

        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetAsync(act.Id, AsFitter));
    }

    // ---------- Отчёт об использовании (выданное «в работу») ----------

    private static SaveActRequest Report(int employee, params ActLineRequest[] lines) =>
        new(employee, null, null, null, null, lines, ActKind.UsageReport);

    private decimal Held(int employee, CustodyKind kind, int item = Bearing) =>
        _stock.EmployeeBalances.Where(b => b.EmployeeId == employee && b.ItemId == item && b.Kind == kind).Sum(b => b.Quantity);

    [Fact]
    public async Task Отчёт_сотрудника_подтверждает_кладовщик_и_позиции_остаются_в_резерве()
    {
        await GiveAsync(Fitter, Bearing, 5, CustodyKind.Work);

        var report = await _service.CreateAsync(Report(Fitter, Line(3, equipment: Pump)), AsFitter);
        Assert.StartsWith("ОИ-", report.Number);
        report = await _service.SubmitAsync(report.Id, AsFitter);
        Assert.Equal(ActStatus.Pending, report.Status);
        Assert.Equal("кладовщик", report.ApproverName);

        // Руководитель отчёт видит, но подтверждает кладовщик
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.ApproveAsync(report.Id, AsBoss));
        Assert.Equal(1, await _service.CountToApproveAsync(AsKeeper));
        Assert.Single(await _service.ListAsync(ActScope.ToApprove, null, null, AsKeeper));

        report = await _service.ApproveAsync(report.Id, AsKeeper);
        Assert.Equal((ActStatus.Approved, "Иванов И."), (report.Status, report.DecidedBy));
        Assert.Null(report.DocumentId);                          // списание — сводным актом кладовщика
        Assert.Equal(5, Held(Fitter, CustodyKind.Work));        // ещё числится, но 3 — в резерве

        // Зарезервированное по отчёту вернуть нельзя — только свободные 2
        await Assert.ThrowsAsync<InsufficientStockException>(() => _stockService.PostDocumentAsync(new CreateDocumentRequest(
            MovementType.Return, null, null, null, null, [new DocumentLineRequest(Bearing, 3, null, Cell)], ReceivedById: Fitter)));
        await _stockService.PostDocumentAsync(new CreateDocumentRequest(
            MovementType.Return, null, null, null, null, [new DocumentLineRequest(Bearing, 2, null, Cell)], ReceivedById: Fitter));
        Assert.Equal(3, Held(Fitter, CustodyKind.Work));
    }

    [Fact]
    public async Task Отчёт_внесённый_кладовщиком_подтверждён_сразу()
    {
        await GiveAsync(Fitter, Bearing, 2, CustodyKind.Work);

        var report = await _service.CreateAsync(Report(Fitter, Line(2, usage: "насос Н-3, сальник")), AsKeeper);
        report = await _service.SubmitAsync(report.Id, AsKeeper);

        Assert.Equal(ActStatus.Approved, report.Status);
        Assert.Equal(0, await _service.CountToApproveAsync(AsKeeper));
    }

    [Fact]
    public async Task Вне_учёта_по_отчёту_списывается_сразу_при_подтверждении()
    {
        await GiveAsync(Fitter, Bearing, 4, CustodyKind.Work, offBook: true);

        var report = await _service.CreateAsync(Report(Fitter, Line(3)), AsFitter);
        await _service.SubmitAsync(report.Id, AsFitter);
        report = await _service.ApproveAsync(report.Id, AsKeeper);

        Assert.NotNull(report.DocumentId);
        Assert.Equal(1, Held(Fitter, CustodyKind.Work));
        // Резерва нет — остаток можно вернуть
        await _stockService.PostDocumentAsync(new CreateDocumentRequest(
            MovementType.Return, null, null, null, null, [new DocumentLineRequest(Bearing, 1, null, Cell)], ReceivedById: Fitter));
    }

    [Fact]
    public async Task Кладовщик_возвращает_отчёт_с_замечанием()
    {
        await GiveAsync(Fitter, Bearing, 1, CustodyKind.Work);
        var report = await _service.CreateAsync(Report(Fitter, Line(1)), AsFitter);
        await _service.SubmitAsync(report.Id, AsFitter);

        report = await _service.ReturnAsync(report.Id, "Укажите оборудование", AsKeeper);

        Assert.Equal((ActStatus.Returned, "Укажите оборудование", "Иванов И."), (report.Status, report.ReturnRemark, report.DecidedBy));
        Assert.True(report.Can.Edit);
    }

    // ---------- Сводный акт кладовщика за месяц ----------

    private async Task<int> ConfirmedReportAsync(int employee, decimal qty, int item = Bearing, string usage = "замена подшипника")
    {
        var report = await _service.CreateAsync(Report(employee, Line(qty, item: item, usage: usage)), AsKeeper);
        report = await _service.SubmitAsync(report.Id, AsKeeper);   // внесено кладовщиком — подтверждено сразу
        Assert.Equal(ActStatus.Approved, report.Status);
        return report.Id;
    }

    private static SaveSummaryRequest Summary(params int[] reports) =>
        new(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), null, null, reports);

    [Fact]
    public async Task Сводный_акт_списывает_выбранные_отчёты_документом_на_каждого_сотрудника()
    {
        await GiveAsync(Fitter, Bearing, 5, CustodyKind.Work);
        await GiveAsync(Other, Belt, 2, CustodyKind.Work);
        var r1 = await ConfirmedReportAsync(Fitter, 3);
        var r2 = await ConfirmedReportAsync(Other, 2, Belt, "транспортёр");
        var r3 = await ConfirmedReportAsync(Fitter, 1);                       // в следующий месяц

        Assert.Equal(3, (await _service.ListSummaryCandidatesAsync(null, AsKeeper)).Count);
        var summary = await _service.CreateSummaryAsync(Summary(r1, r2), AsKeeper);
        Assert.StartsWith("СА-", summary.Number);
        Assert.Equal((ActStatus.Draft, 2, 2), (summary.Status, summary.Lines.Count, summary.Reports!.Count));
        Assert.Equal(["Петров С.", "Козлов А."], summary.Lines.Select(l => l.EmployeeName));
        Assert.Single(await _service.ListSummaryCandidatesAsync(null, AsKeeper));   // r3 — свободен

        summary = await _service.PostSummaryAsync(summary.Id, AsKeeper);

        Assert.Equal(ActStatus.Approved, summary.Status);
        Assert.Equal(2, summary.Documents!.Count);                              // на каждого сотрудника
        Assert.Equal(2, Held(Fitter, CustodyKind.Work));                        // 5 − 3
        Assert.Equal(0, Held(Other, CustodyKind.Work, Belt));
        Assert.True((await _service.GetAsync(r1, AsKeeper)).WrittenOff);
        Assert.False((await _service.GetAsync(r3, AsKeeper)).WrittenOff);

        // Резерв r3 (1 шт) остаётся, свободная 1 шт возвращается
        await Assert.ThrowsAsync<InsufficientStockException>(() => _stockService.PostDocumentAsync(new CreateDocumentRequest(
            MovementType.Return, null, null, null, null, [new DocumentLineRequest(Bearing, 2, null, Cell)], ReceivedById: Fitter)));
        await _stockService.PostDocumentAsync(new CreateDocumentRequest(
            MovementType.Return, null, null, null, null, [new DocumentLineRequest(Bearing, 1, null, Cell)], ReceivedById: Fitter));

        summary = await _service.SetExternalNumberAsync(summary.Id, "АКТ-1С № 154", AsKeeper);
        Assert.Equal("АКТ-1С № 154", summary.ExternalNumber);
    }

    [Fact]
    public async Task Сводный_акт_делится_по_МОЛ_партий()
    {
        await GiveAsync(Fitter, Bearing, 2);              // МОЛ не указан (кладовщиков ещё не было)
        _stock.Storekeepers.AddRange([Keeper, Boss]);
        await GiveAsync(Fitter, Belt, 3, mol: Boss);      // числится за кладовщиком Сидоровым
        var report = await _service.CreateAsync(Request(Fitter, Line(1), Line(2, item: Belt)), AsKeeper);
        report = await _service.SubmitAsync(report.Id, AsKeeper);

        var summary = await _service.CreateSummaryAsync(Summary(report.Id), AsKeeper);
        Assert.Equal([(Bearing, (int?)null, (string?)null), (Belt, Boss, "Сидоров О.")],
            summary.Lines.OrderBy(l => l.ItemId).Select(l => (l.ItemId, l.MolId, l.Mol)));

        summary = await _service.PostSummaryAsync(summary.Id, AsKeeper);
        Assert.Equal(2, summary.Documents!.Count);   // один сотрудник, два МОЛ — два документа списания
        Assert.Contains(_stock.Documents, d => d.Type == MovementType.WriteOff && d.Comment!.Contains("МОЛ Сидоров О."));
    }

    [Fact]
    public async Task Отчёт_только_в_одном_сводном_акте_и_только_подтверждённый()
    {
        await GiveAsync(Fitter, Bearing, 5, CustodyKind.Work);
        var confirmed = await ConfirmedReportAsync(Fitter, 1);
        var pending = await _service.CreateAsync(Report(Fitter, Line(1)), AsFitter);
        await _service.SubmitAsync(pending.Id, AsFitter);

        var first = await _service.CreateSummaryAsync(Summary(confirmed), AsKeeper);
        await Assert.ThrowsAsync<DomainException>(() => _service.CreateSummaryAsync(Summary(confirmed), AsKeeper));
        await Assert.ThrowsAsync<DomainException>(() => _service.CreateSummaryAsync(Summary(pending.Id), AsKeeper));

        // Удалили черновик — отчёт снова свободен
        await _service.DeleteAsync(first.Id, AsKeeper);
        Assert.Single(await _service.ListSummaryCandidatesAsync(null, AsKeeper));
    }

    [Fact]
    public async Task Сводный_акт_составляет_и_видит_только_кладовщик()
    {
        await GiveAsync(Fitter, Bearing, 1, CustodyKind.Work);
        var report = await ConfirmedReportAsync(Fitter, 1);

        await Assert.ThrowsAsync<ForbiddenException>(() => _service.CreateSummaryAsync(Summary(report), AsBoss));
        var summary = await _service.CreateSummaryAsync(Summary(report), AsKeeper);
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetAsync(summary.Id, AsBoss));
        Assert.DoesNotContain(await _service.ListAsync(ActScope.Visible, null, null, AsBoss), a => a.Kind == ActKind.Summary);
        await Assert.ThrowsAsync<DomainException>(() => _service.SubmitAsync(summary.Id, AsKeeper));
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
        if (filter.ApproverId is not null || filter.ReportsToConfirm)
            query = query.Where(a => ToDecide(a, filter.ApproverId, filter.ReportsToConfirm));
        if (filter.Kind is ActKind kind)
            query = query.Where(a => a.Kind == kind);
        if (filter.Status is ActStatus status)
            query = query.Where(a => a.Status == status);
        if (filter.EmployeeId is int employeeId)
            query = query.Where(a => a.EmployeeId == employeeId);

        return Task.FromResult<IReadOnlyList<ActHeader>>(
            query.OrderByDescending(a => a.Id).Take(filter.Take).Select(a => new ActHeader(a, a.Lines.Count)).ToList());
    }

    public Task<int> CountToApproveAsync(int? approverId, bool reportsToConfirm, CancellationToken ct = default)
    {
        foreach (var act in stock.Acts)
            Populate(act);
        return Task.FromResult(stock.Acts.Count(a => ToDecide(a, approverId, reportsToConfirm)));
    }

    private static bool ToDecide(WriteOffAct a, int? approverId, bool reportsToConfirm) =>
        a.Status == ActStatus.Pending
        && ((a.Kind == ActKind.WriteOff && approverId is int me && IsApprover(a, me))
            || (reportsToConfirm && a.Kind == ActKind.UsageReport));

    public Task<IReadOnlyList<WriteOffAct>> ListSummaryCandidatesAsync(int? summaryId, CancellationToken ct = default)
    {
        foreach (var act in stock.Acts)
            Populate(act);
        return Task.FromResult<IReadOnlyList<WriteOffAct>>(stock.Acts
            .Where(a => a.Kind == ActKind.UsageReport && a.Status == ActStatus.Approved
                && (a.SummaryActId is null || a.SummaryActId == summaryId)
                && a.Lines.Any(l => l.DocumentId is null && l.Batch?.OffBook != true))
            .ToList());
    }

    public Task<IReadOnlyList<WriteOffAct>> GetManyAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default)
    {
        var found = stock.Acts.Where(a => ids.Contains(a.Id)).ToList();
        found.ForEach(Populate);
        return Task.FromResult<IReadOnlyList<WriteOffAct>>(found);
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
        foreach (var act in stock.Acts)
        {
            act.SummaryActId = act.SummaryAct?.Id ?? act.SummaryActId;
            foreach (var line in act.Lines.Where(l => l.Document is not null))
                line.DocumentId = line.Document!.Id;
        }
    }

    private static bool IsApprover(WriteOffAct act, int approverId) =>
        act.Employee.ManagerId == approverId || (act.Employee.ManagerId is null && act.EmployeeId == approverId);

    /// <summary>Навигационные свойства, как после Include в EF.</summary>
    private void Populate(WriteOffAct act)
    {
        act.Employee = stock.Employees.First(e => e.Id == act.EmployeeId);
        act.SummaryAct = stock.Acts.FirstOrDefault(a => a.Id == act.SummaryActId) ?? act.SummaryAct;
        if (act.Kind == ActKind.Summary)
        {
            act.Reports = stock.Acts.Where(a => a.SummaryActId == act.Id || a.SummaryAct == act).ToList();
            foreach (var report in act.Reports)
            {
                report.SummaryAct = act;
                report.Employee = stock.Employees.First(e => e.Id == report.EmployeeId);
                foreach (var line in report.Lines)
                {
                    line.Item = stock.Items.First(i => i.Id == line.ItemId);
                    line.Equipment = stock.Equipment.FirstOrDefault(e => e.Id == line.EquipmentId);
                    line.Batch = stock.Batches.FirstOrDefault(b => b.Id == line.BatchId);
                }
            }
        }
        foreach (var line in act.Lines)
        {
            line.Item = stock.Items.First(i => i.Id == line.ItemId);
            line.Equipment = stock.Equipment.FirstOrDefault(e => e.Id == line.EquipmentId);
            line.Batch = stock.Batches.FirstOrDefault(b => b.Id == line.BatchId);
        }
    }
}
