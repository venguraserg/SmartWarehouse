using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Application.Services;

/// <summary>Кто работает с запросами на выдачу (из учётной записи).</summary>
/// <param name="EmployeeId">Сотрудник, к которому привязана учётная запись.</param>
/// <param name="CanIssue">Кладовщик, администратор или старший смены: видит все запросы, выдаёт и отклоняет.</param>
/// <param name="Visible">Чьи запросы видны: null — все; руководитель — свои и подчинённых; остальные — свои.</param>
public record IssueRequestActor(int? EmployeeId, string? DisplayName, bool CanIssue, IReadOnlyCollection<int>? Visible);

public interface IIssueRequestService
{
    Task<IReadOnlyList<IssueRequestSummaryDto>> ListAsync(IssueRequestStatus? status, IssueRequestActor actor, CancellationToken ct = default);

    Task<IssueRequestDto> GetAsync(int id, IssueRequestActor actor, CancellationToken ct = default);

    /// <summary>Новый запрос: позиции из остатков склада.</summary>
    Task<IssueRequestDto> CreateAsync(CreateIssueRequestRequest request, IssueRequestActor actor, CancellationToken ct = default);

    /// <summary>Сотрудник отменяет свой открытый запрос.</summary>
    Task<IssueRequestDto> CancelAsync(int id, IssueRequestActor actor, CancellationToken ct = default);

    /// <summary>Кладовщик (старший смены) отклоняет запрос с причиной.</summary>
    Task<IssueRequestDto> RejectAsync(int id, string? reason, IssueRequestActor actor, CancellationToken ct = default);

    /// <summary>Проверка перед проведением выдачи по запросу: запрос открыт и выдаётся тому, кто просил.</summary>
    Task EnsureCanIssueAsync(int id, int? employeeId, IssueRequestActor actor, CancellationToken ct = default);

    /// <summary>Выдача по запросу проведена — запрос закрывается документом выдачи.</summary>
    Task MarkIssuedAsync(int id, long documentId, IssueRequestActor actor, CancellationToken ct = default);

    /// <summary>Сколько запросов ждёт выдачи — для меню тех, кто выдаёт (остальным 0).</summary>
    Task<int> CountOpenAsync(IssueRequestActor actor, CancellationToken ct = default);
}

/// <summary>
/// Запросы на выдачу. Сотрудник выбирает из остатков, кладовщик собирает и оформляет выдачу с заполненными строками.
/// Остатки запрос не резервирует; выдать можно меньше — что есть, запрос всё равно закрывается.
/// </summary>
public class IssueRequestService(IIssueRequestRepository repo, IStockRepository stock, TimeProvider clock) : IIssueRequestService
{
    public const int MaxLines = 100;

    public async Task<IReadOnlyList<IssueRequestSummaryDto>> ListAsync(IssueRequestStatus? status, IssueRequestActor actor, CancellationToken ct = default)
    {
        var requests = await repo.ListAsync(actor.Visible, status, 500, ct);
        return requests
            .Select(r => new IssueRequestSummaryDto(
                r.Id, r.Number, r.Status, r.EmployeeId, r.Employee.ShortName, r.CreatedAt, r.Lines.Count, r.Comment,
                r.DocumentId, r.Document?.Number))
            .ToList();
    }

    public async Task<IssueRequestDto> GetAsync(int id, IssueRequestActor actor, CancellationToken ct = default) =>
        await ToDtoAsync(await LoadVisibleAsync(id, actor, ct), actor, ct);

    public async Task<IssueRequestDto> CreateAsync(CreateIssueRequestRequest request, IssueRequestActor actor, CancellationToken ct = default)
    {
        var employeeId = request.EmployeeId ?? actor.EmployeeId
            ?? throw new DomainException("Учётная запись не привязана к сотруднику — запрос не на кого оформить.");
        if (!actor.CanIssue && employeeId != actor.EmployeeId)
            throw new ForbiddenException("Запрос на выдачу можно оформить только на себя.");

        var employee = await stock.GetEmployeeAsync(employeeId, ct)
            ?? throw new NotFoundException($"Сотрудник {employeeId} не найден.");
        if (!employee.IsActive)
            throw new DomainException($"{employee.ShortName} отмечен как неработающий — выдавать ему нельзя.");

        var lines = (request.Lines ?? [])
            .GroupBy(l => l.ItemId)
            .Select(g => (ItemId: g.Key, Quantity: g.Sum(l => l.Quantity)))
            .ToList();
        if (lines.Count == 0)
            throw new DomainException("Добавьте в запрос хотя бы одну позицию.");
        if (lines.Count > MaxLines)
            throw new DomainException($"В одном запросе — не больше {MaxLines} позиций.");

        var items = await stock.GetItemsAsync(lines.Select(l => l.ItemId), ct);
        foreach (var (itemId, quantity) in lines)
        {
            if (!items.TryGetValue(itemId, out var item) || !item.IsActive)
                throw new DomainException($"Позиция {itemId} не найдена или снята с учёта.");
            if (quantity <= 0)
                throw new DomainException($"{item.Name}: количество должно быть больше нуля.");
            if (!item.Unit.IsFractional && quantity != decimal.Truncate(quantity))
                throw new DomainException($"{item.Name}: «{item.Unit.Code}» — только целое количество.");

            var onHand = (await stock.GetBalancesForItemAsync(itemId, ct)).Sum(b => b.Quantity);
            if (quantity > onHand)
                throw new DomainException(onHand <= 0
                    ? $"{item.Name}: на складе нет."
                    : $"{item.Name}: на складе только {onHand:0.###} {item.Unit.Code}.");
        }

        var entity = new IssueRequest
        {
            EmployeeId = employee.Id,
            Comment = Clean(request.Comment),
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = actor.DisplayName,
            Status = IssueRequestStatus.Open
        };
        var lineNo = 0;
        foreach (var (itemId, quantity) in lines)
            entity.Lines.Add(new IssueRequestLine { LineNo = ++lineNo, ItemId = itemId, Quantity = quantity });

        repo.Add(entity);
        await repo.SaveChangesAsync(ct);
        return await GetAsync(entity.Id, actor, ct);
    }

    public async Task<IssueRequestDto> CancelAsync(int id, IssueRequestActor actor, CancellationToken ct = default)
    {
        var request = await LoadVisibleAsync(id, actor, ct);
        EnsureOpen(request);
        if (request.EmployeeId != actor.EmployeeId)
            throw new ForbiddenException("Отменить запрос может только тот, кому он нужен. Кладовщик — отклоняет.");
        Close(request, IssueRequestStatus.Cancelled, actor, note: null);
        await repo.SaveChangesAsync(ct);
        return await ToDtoAsync(request, actor, ct);
    }

    public async Task<IssueRequestDto> RejectAsync(int id, string? reason, IssueRequestActor actor, CancellationToken ct = default)
    {
        EnsureIssuer(actor);
        var request = await LoadVisibleAsync(id, actor, ct);
        EnsureOpen(request);
        var note = Clean(reason) ?? throw new DomainException("Укажите причину — сотрудник её увидит.");
        Close(request, IssueRequestStatus.Rejected, actor, note);
        await repo.SaveChangesAsync(ct);
        return await ToDtoAsync(request, actor, ct);
    }

    public async Task EnsureCanIssueAsync(int id, int? employeeId, IssueRequestActor actor, CancellationToken ct = default)
    {
        EnsureIssuer(actor);
        var request = await LoadVisibleAsync(id, actor, ct);
        EnsureOpen(request);
        if (employeeId != request.EmployeeId)
            throw new DomainException($"Запрос {request.Number} — от {request.Employee.ShortName}: выдача по нему оформляется на него.");
    }

    public async Task MarkIssuedAsync(int id, long documentId, IssueRequestActor actor, CancellationToken ct = default)
    {
        var request = await LoadVisibleAsync(id, actor, ct);
        EnsureOpen(request);
        request.DocumentId = documentId;
        Close(request, IssueRequestStatus.Issued, actor, note: null);
        await repo.SaveChangesAsync(ct);
    }

    public Task<int> CountOpenAsync(IssueRequestActor actor, CancellationToken ct = default) =>
        actor.CanIssue ? repo.CountOpenAsync(ct) : Task.FromResult(0);

    // ---------- Вспомогательное ----------

    private async Task<IssueRequest> LoadVisibleAsync(int id, IssueRequestActor actor, CancellationToken ct)
    {
        var request = await repo.GetAsync(id, ct) ?? throw new NotFoundException($"Запрос {IssueRequest.FormatNumber(id)} не найден.");
        if (actor.Visible is not null && !actor.Visible.Contains(request.EmployeeId))
            throw new ForbiddenException("Нет доступа к этому запросу.");
        return request;
    }

    private static void EnsureIssuer(IssueRequestActor actor)
    {
        if (!actor.CanIssue)
            throw new ForbiddenException("Запросы обрабатывают кладовщик и старший смены.");
    }

    private static void EnsureOpen(IssueRequest request)
    {
        if (request.Status != IssueRequestStatus.Open)
            throw new DomainException($"Запрос {request.Number} уже {StatusText(request.Status)}.");
    }

    private void Close(IssueRequest request, IssueRequestStatus status, IssueRequestActor actor, string? note)
    {
        request.Status = status;
        request.ClosedAt = clock.GetUtcNow();
        request.ClosedBy = actor.DisplayName;
        request.CloseNote = note;
    }

    private static string StatusText(IssueRequestStatus status) => status switch
    {
        IssueRequestStatus.Issued => "выдан",
        IssueRequestStatus.Cancelled => "отменён",
        IssueRequestStatus.Rejected => "отклонён",
        _ => "открыт"
    };

    private async Task<IssueRequestDto> ToDtoAsync(IssueRequest r, IssueRequestActor actor, CancellationToken ct)
    {
        var open = r.Status == IssueRequestStatus.Open;

        // Сколько выдано — по документу выдачи.
        var issued = r.Document?.Lines
            .GroupBy(l => l.ItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        var lines = new List<IssueRequestLineDto>();
        foreach (var l in r.Lines.OrderBy(l => l.LineNo))
        {
            IReadOnlyList<IssueRequestPlaceDto> places = [];
            decimal onHand = 0;
            if (open)
            {
                var balances = await stock.GetBalancesForItemAsync(l.ItemId, ct);
                onHand = balances.Sum(b => b.Quantity);
                // Где лежит — тем, кто собирает; по коду места, чтобы обходить склад по порядку.
                if (actor.CanIssue)
                    places = balances
                        .GroupBy(b => (b.LocationId, b.Location.FullCode, b.Location.Name))
                        .Select(g => new IssueRequestPlaceDto(g.Key.LocationId, g.Key.FullCode, g.Key.Name, g.Sum(b => b.Quantity)))
                        .OrderBy(p => p.LocationCode, StringComparer.Ordinal)
                        .ToList();
            }
            lines.Add(new IssueRequestLineDto(
                l.LineNo, l.ItemId, l.Item.Sku, l.Item.Name, l.Item.Unit.Code, l.Quantity,
                issued is null ? null : issued.GetValueOrDefault(l.ItemId), onHand, places));
        }

        return new IssueRequestDto(
            r.Id, r.Number, r.Status, r.EmployeeId, r.Employee.ShortName, r.Employee.Position, r.Comment,
            r.CreatedAt, r.CreatedBy, r.ClosedAt, r.ClosedBy, r.CloseNote, r.DocumentId, r.Document?.Number,
            lines,
            new IssueRequestPermissions(
                Issue: open && actor.CanIssue,
                Reject: open && actor.CanIssue,
                Cancel: open && r.EmployeeId == actor.EmployeeId));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
