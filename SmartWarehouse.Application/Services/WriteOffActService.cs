using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Application.Services;

/// <summary>Кто выполняет действие с актом (из учётной записи).</summary>
/// <param name="EmployeeId">Сотрудник, к которому привязана учётная запись.</param>
/// <param name="IsWarehouse">Кладовщик или администратор: видит все акты и может составить акт за сотрудника.</param>
/// <param name="IsManager">Руководитель: видит акты подчинённых.</param>
public record ActActor(int? EmployeeId, string? DisplayName, bool IsWarehouse, bool IsManager);

public interface IWriteOffActService
{
    Task<IReadOnlyList<ActSummaryDto>> ListAsync(ActScope scope, ActStatus? status, int? employeeId, ActActor actor, CancellationToken ct = default);

    Task<ActDto> GetAsync(int id, ActActor actor, CancellationToken ct = default);

    /// <summary>Новый акт — черновик.</summary>
    Task<ActDto> CreateAsync(SaveActRequest request, ActActor actor, CancellationToken ct = default);

    /// <summary>Изменить черновик или возвращённый акт (становится черновиком).</summary>
    Task<ActDto> UpdateAsync(int id, SaveActRequest request, ActActor actor, CancellationToken ct = default);

    Task DeleteAsync(int id, ActActor actor, CancellationToken ct = default);

    /// <summary>
    /// Отправить на утверждение: позиции резервируются. Если руководителя нет и отправляет сам сотрудник —
    /// акт сразу утверждается (сотрудник утверждает свои акты сам).
    /// </summary>
    Task<ActDto> SubmitAsync(int id, ActActor actor, CancellationToken ct = default);

    /// <summary>Отозвать с утверждения — снова черновик, резерв снят.</summary>
    Task<ActDto> RecallAsync(int id, ActActor actor, CancellationToken ct = default);

    /// <summary>Утвердить: проводится документ списания с сотрудника.</summary>
    Task<ActDto> ApproveAsync(int id, ActActor actor, CancellationToken ct = default);

    /// <summary>Вернуть на доработку с замечанием.</summary>
    Task<ActDto> ReturnAsync(int id, string? remark, ActActor actor, CancellationToken ct = default);

    /// <summary>Сколько актов ждут утверждения текущим пользователем.</summary>
    Task<int> CountToApproveAsync(ActActor actor, CancellationToken ct = default);
}

/// <summary>
/// Акты на списание: сотрудник отчитывается, куда израсходовал полученное, руководитель утверждает.
/// Остатки меняются только при утверждении — проведением документа списания (через <see cref="StockService"/>),
/// акт и документ сохраняются одной транзакцией.
/// </summary>
public class WriteOffActService(
    IWriteOffActRepository acts,
    IStockRepository stockRepo,
    IEmployeeRepository employees,
    StockService stock,
    TimeProvider clock) : IWriteOffActService
{
    public const int MaxLines = 200;
    public const int MaxUsageLength = 500;

    // ---------- Чтение ----------

    public async Task<IReadOnlyList<ActSummaryDto>> ListAsync(
        ActScope scope, ActStatus? status, int? employeeId, ActActor actor, CancellationToken ct = default)
    {
        ActFilter filter;
        if (scope == ActScope.ToApprove)
        {
            if (actor.EmployeeId is not int me)
                return [];
            filter = new ActFilter(null, me, ActStatus.Pending, employeeId, 500);
        }
        else
        {
            filter = new ActFilter(await VisibleEmployeesAsync(actor, ct), null, status, employeeId, 500);
        }

        var headers = await acts.ListAsync(filter, ct);
        return headers.Select(h => ToSummary(h.Act, h.LineCount)).ToList();
    }

    public async Task<ActDto> GetAsync(int id, ActActor actor, CancellationToken ct = default)
    {
        var act = await LoadVisibleAsync(id, actor, ct);
        return ToDto(act, actor);
    }

    public async Task<int> CountToApproveAsync(ActActor actor, CancellationToken ct = default) =>
        actor.EmployeeId is int me ? await acts.CountToApproveAsync(me, ct) : 0;

    // ---------- Черновик ----------

    public async Task<ActDto> CreateAsync(SaveActRequest request, ActActor actor, CancellationToken ct = default)
    {
        if (!actor.IsWarehouse && actor.EmployeeId != request.EmployeeId)
            throw new ForbiddenException("Акт на списание составляет сам сотрудник (или кладовщик за него).");

        var employee = await employees.GetAsync(request.EmployeeId, ct)
            ?? throw new NotFoundException($"Сотрудник {request.EmployeeId} не найден.");

        var now = clock.GetUtcNow();
        var act = new WriteOffAct
        {
            EmployeeId = employee.Id,
            Status = ActStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = Clean(actor.DisplayName)
        };
        await ApplyAsync(act, request, employee, ct);

        acts.Add(act);
        await acts.SaveChangesAsync(ct);
        return await GetAsync(act.Id, actor, ct);
    }

    public async Task<ActDto> UpdateAsync(int id, SaveActRequest request, ActActor actor, CancellationToken ct = default)
    {
        var act = await LoadVisibleAsync(id, actor, ct);
        EnsureCanEdit(act, actor);
        if (request.EmployeeId != act.EmployeeId)
            throw new DomainException("Сотрудника в акте сменить нельзя — составьте новый акт.");

        await ApplyAsync(act, request, act.Employee, ct);
        act.Status = ActStatus.Draft;
        act.UpdatedAt = clock.GetUtcNow();

        await acts.SaveChangesAsync(ct);
        return await GetAsync(act.Id, actor, ct);
    }

    public async Task DeleteAsync(int id, ActActor actor, CancellationToken ct = default)
    {
        var act = await LoadVisibleAsync(id, actor, ct);
        EnsureCanEdit(act, actor);
        acts.Remove(act);
        await acts.SaveChangesAsync(ct);
    }

    // ---------- Утверждение ----------

    public async Task<ActDto> SubmitAsync(int id, ActActor actor, CancellationToken ct = default)
    {
        var act = await LoadVisibleAsync(id, actor, ct);
        EnsureCanEdit(act, actor);
        if (act.Lines.Count == 0)
            throw new DomainException("В акте нет строк.");

        // За время черновика подотчёт мог измениться (возврат, другой акт) — проверяем снова.
        await CheckAvailableAsync(act.Employee, act.Lines.Select(l => (l.Item, l.Quantity)), act.Id, ct);

        var now = clock.GetUtcNow();
        act.Status = ActStatus.Pending;
        act.SubmittedAt = now;
        act.UpdatedAt = now;

        // Руководителя нет — сотрудник утверждает свои акты сам, без лишнего шага.
        if (ApproverOf(act.Employee).Id == act.EmployeeId && actor.EmployeeId == act.EmployeeId)
            await ApproveCoreAsync(act, ct);

        await acts.SaveChangesAsync(ct);
        return await GetAsync(act.Id, actor, ct);
    }

    public async Task<ActDto> RecallAsync(int id, ActActor actor, CancellationToken ct = default)
    {
        var act = await LoadVisibleAsync(id, actor, ct);
        if (!CanRecall(act, actor))
            throw act.Status == ActStatus.Pending
                ? new ForbiddenException("Отозвать акт может сам сотрудник или кладовщик.")
                : new DomainException("Отозвать можно только акт, который на утверждении.");

        act.Status = ActStatus.Draft;
        act.UpdatedAt = clock.GetUtcNow();
        await acts.SaveChangesAsync(ct);
        return await GetAsync(act.Id, actor, ct);
    }

    public async Task<ActDto> ApproveAsync(int id, ActActor actor, CancellationToken ct = default)
    {
        var act = await LoadVisibleAsync(id, actor, ct);
        EnsureCanDecide(act, actor);

        await ApproveCoreAsync(act, ct);
        await acts.SaveChangesAsync(ct);
        return await GetAsync(act.Id, actor, ct);
    }

    public async Task<ActDto> ReturnAsync(int id, string? remark, ActActor actor, CancellationToken ct = default)
    {
        var act = await LoadVisibleAsync(id, actor, ct);
        EnsureCanDecide(act, actor);

        var text = Clean(remark) ?? throw new DomainException("Напишите замечание: что нужно исправить в акте.");
        if (text.Length > 1000)
            throw new DomainException("Замечание — не длиннее 1000 символов.");

        var approver = ApproverOf(act.Employee);
        var now = clock.GetUtcNow();
        act.Status = ActStatus.Returned;
        act.ReturnRemark = text;
        act.DecidedById = approver.Id;
        act.DecidedBy = approver.ShortName;
        act.DecidedByPosition = approver.Position;
        act.DecidedAt = now;
        act.UpdatedAt = now;

        await acts.SaveChangesAsync(ct);
        return await GetAsync(act.Id, actor, ct);
    }

    /// <summary>Проводит документ списания и отмечает акт утверждённым (без сохранения).</summary>
    private async Task ApproveCoreAsync(WriteOffAct act, CancellationToken ct)
    {
        var approver = ApproverOf(act.Employee);

        // Если всё израсходовано на одно оборудование — оно же попадает в документ.
        var equipment = act.Lines.Select(l => l.EquipmentId).Distinct().ToList();
        var comment = $"По акту на списание {act.Number}";
        if (act.ExternalNumber is not null)
            comment += $" ({act.ExternalNumber})";

        var request = new CreateDocumentRequest(
            MovementType.WriteOff,
            act.Number,
            equipment.Count == 1 ? equipment[0] : null,
            null,
            comment,
            act.Lines.OrderBy(l => l.LineNo).Select(l => new DocumentLineRequest(l.ItemId, l.Quantity, null, null)).ToList(),
            PerformedById: approver.Id,
            ReceivedById: act.EmployeeId);

        var staged = await stock.StageAsync(request, exceptActId: act.Id, ct);

        var now = clock.GetUtcNow();
        act.Document = staged.Document;
        staged.Document.Act = act;
        act.Status = ActStatus.Approved;
        act.DecidedById = approver.Id;
        act.DecidedBy = approver.ShortName;
        act.DecidedByPosition = approver.Position;
        act.DecidedAt = now;
        act.UpdatedAt = now;
        act.ReturnRemark = null;
    }

    // ---------- Проверки ----------

    /// <summary>Заполняет шапку и строки акта из запроса, проверяя всё, что можно проверить заранее.</summary>
    private async Task ApplyAsync(WriteOffAct act, SaveActRequest request, Employee employee, CancellationToken ct)
    {
        var lines = request.Lines ?? Array.Empty<ActLineRequest>();
        if (lines.Count == 0)
            throw new DomainException("Добавьте хотя бы одну строку: что израсходовано и куда.");
        if (lines.Count > MaxLines)
            throw new DomainException($"В акте не может быть больше {MaxLines} строк — разбейте на несколько актов.");

        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        if (request.PeriodFrom is DateOnly from && request.PeriodTo is DateOnly to && from > to)
            throw new DomainException("Начало периода позже его окончания.");
        if (request.PeriodTo > today || request.PeriodFrom > today)
            throw new DomainException("Период акта не может быть в будущем.");

        var externalNumber = Clean(request.ExternalNumber);
        if (externalNumber?.Length > 100)
            throw new DomainException("Основание — не длиннее 100 символов.");
        var comment = Clean(request.Comment);
        if (comment?.Length > 1000)
            throw new DomainException("Комментарий — не длиннее 1000 символов.");

        var items = await stockRepo.GetItemsAsync(lines.Select(l => l.ItemId).Distinct(), ct);
        var checkedEquipment = new HashSet<int>();

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var n = i + 1;

            if (!items.TryGetValue(line.ItemId, out var item))
                throw new NotFoundException($"Строка {n}: позиция {line.ItemId} не найдена.");
            if (line.Quantity <= 0)
                throw new DomainException($"Строка {n}: количество должно быть больше нуля.");
            if (!item.Unit.IsFractional && line.Quantity != decimal.Truncate(line.Quantity))
                throw new DomainException($"Строка {n}: «{item.Name}» учитывается в «{item.Unit.Code}», количество должно быть целым.");
            if (!item.IsActive)
                throw new DomainException($"Строка {n}: позиция «{item.Name}» неактивна, операции с ней запрещены.");

            if (line.EquipmentId is int equipmentId && checkedEquipment.Add(equipmentId)
                && !await stockRepo.EquipmentExistsAsync(equipmentId, ct))
                throw new NotFoundException($"Строка {n}: оборудование {equipmentId} не найдено.");

            var usage = Clean(line.Usage);
            if (line.EquipmentId is null && usage is null)
                throw new DomainException($"Строка {n} («{item.Name}»): укажите, куда использовано — оборудование или описание.");
            if (usage?.Length > MaxUsageLength)
                throw new DomainException($"Строка {n}: описание «куда использовано» — не длиннее {MaxUsageLength} символов.");

            if (line.UsedOn is DateOnly usedOn)
            {
                if (usedOn > today)
                    throw new DomainException($"Строка {n}: дата использования не может быть в будущем.");
                if (usedOn < request.PeriodFrom || usedOn > request.PeriodTo)
                    throw new DomainException($"Строка {n}: дата использования вне периода акта.");
            }
        }

        await CheckAvailableAsync(employee, lines.Select(l => (items[l.ItemId], l.Quantity)), act.Id == 0 ? null : act.Id, ct);

        act.PeriodFrom = request.PeriodFrom;
        act.PeriodTo = request.PeriodTo;
        act.ExternalNumber = externalNumber;
        act.Comment = comment;

        act.Lines.Clear();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            act.Lines.Add(new WriteOffActLine
            {
                LineNo = i + 1,
                ItemId = line.ItemId,
                Quantity = line.Quantity,
                EquipmentId = line.EquipmentId,
                Usage = Clean(line.Usage),
                UsedOn = line.UsedOn
            });
        }
    }

    /// <summary>
    /// Хватает ли за сотрудником свободного (не зарезервированного другими актами) количества.
    /// Одна позиция может быть в нескольких строках — суммируется.
    /// </summary>
    private async Task CheckAvailableAsync(
        Employee employee, IEnumerable<(Item Item, decimal Quantity)> lines, int? exceptActId, CancellationToken ct)
    {
        foreach (var group in lines.GroupBy(l => l.Item.Id))
        {
            var item = group.First().Item;
            var requested = group.Sum(l => l.Quantity);
            var balance = (await stockRepo.GetEmployeeBalanceAsync(item.Id, employee.Id, ct))?.Quantity ?? 0;
            var reserved = await stockRepo.GetReservedAsync(item.Id, employee.Id, exceptActId, ct);
            var available = Math.Max(0, balance - reserved);
            if (available < requested)
                throw new InsufficientStockException(item.Sku, employee.ShortName, available, requested, isEmployee: true, reserved: reserved);
        }
    }

    private async Task<WriteOffAct> LoadVisibleAsync(int id, ActActor actor, CancellationToken ct)
    {
        var act = await acts.GetAsync(id, ct) ?? throw new NotFoundException($"Акт {id} не найден.");
        if (actor.IsWarehouse || actor.EmployeeId == ApproverOf(act.Employee).Id)
            return act;

        var visible = await VisibleEmployeesAsync(actor, ct);
        if (visible is not null && !visible.Contains(act.EmployeeId))
            throw new ForbiddenException("Нет доступа к этому акту.");
        return act;
    }

    /// <returns>null — все сотрудники (кладовщик, администратор); иначе — чьи акты видны.</returns>
    private async Task<IReadOnlyCollection<int>?> VisibleEmployeesAsync(ActActor actor, CancellationToken ct)
    {
        if (actor.IsWarehouse)
            return null;
        if (actor.EmployeeId is not int me)
            return Array.Empty<int>();
        if (!actor.IsManager)
            return [me];
        return EmployeeService.Team(await employees.ListHierarchyAsync(ct), me);
    }

    /// <summary>Кто утверждает акты сотрудника: непосредственный руководитель, а если его нет — сам сотрудник.</summary>
    internal static Employee ApproverOf(Employee employee) => employee.Manager ?? employee;

    private static bool CanEdit(WriteOffAct act, ActActor actor) =>
        act.IsEditable && (actor.IsWarehouse || actor.EmployeeId == act.EmployeeId);

    private static bool CanRecall(WriteOffAct act, ActActor actor) =>
        act.Status == ActStatus.Pending && (actor.IsWarehouse || actor.EmployeeId == act.EmployeeId);

    private static bool CanDecide(WriteOffAct act, ActActor actor) =>
        act.Status == ActStatus.Pending && actor.EmployeeId is int me && me == ApproverOf(act.Employee).Id;

    private static void EnsureCanEdit(WriteOffAct act, ActActor actor)
    {
        if (!act.IsEditable)
            throw new DomainException(act.Status == ActStatus.Pending
                ? $"Акт {act.Number} на утверждении — сначала отзовите его."
                : $"Акт {act.Number} уже утверждён, изменить его нельзя.");
        if (!CanEdit(act, actor))
            throw new ForbiddenException("Изменять акт может сам сотрудник или кладовщик.");
    }

    private static void EnsureCanDecide(WriteOffAct act, ActActor actor)
    {
        if (act.Status != ActStatus.Pending)
            throw new DomainException($"Акт {act.Number} не на утверждении.");
        if (!CanDecide(act, actor))
            throw new ForbiddenException($"Акт утверждает {ApproverOf(act.Employee).ShortName} — непосредственный руководитель сотрудника.");
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // ---------- Преобразование ----------

    private static ActDto ToDto(WriteOffAct act, ActActor actor)
    {
        var approver = ApproverOf(act.Employee);
        return new ActDto(
            act.Id,
            act.Number,
            act.Status,
            act.EmployeeId,
            act.Employee.ShortName,
            act.Employee.Position,
            act.PeriodFrom,
            act.PeriodTo,
            act.ExternalNumber,
            act.Comment,
            act.CreatedAt,
            act.CreatedBy,
            act.UpdatedAt,
            act.SubmittedAt,
            approver.Id,
            approver.ShortName,
            act.DecidedById,
            act.DecidedBy,
            act.DecidedByPosition,
            act.DecidedAt,
            act.ReturnRemark,
            act.DocumentId,
            act.DocumentId is long documentId ? StockDocument.FormatNumber(MovementType.WriteOff, documentId) : null,
            act.Lines
                .OrderBy(l => l.LineNo)
                .Select(l => new ActLineDto(
                    l.LineNo, l.ItemId, l.Item.Sku, l.Item.Name, l.Item.Unit.Code, l.Quantity,
                    l.EquipmentId, l.Equipment is { } e ? $"{e.Code} — {e.Name}" : null,
                    l.Usage, l.UsedOn))
                .ToList(),
            new ActPermissions(CanEdit(act, actor), CanEdit(act, actor), CanRecall(act, actor), CanDecide(act, actor)));
    }

    private static ActSummaryDto ToSummary(WriteOffAct act, int lineCount) => new(
        act.Id,
        act.Number,
        act.Status,
        act.EmployeeId,
        act.Employee.ShortName,
        act.PeriodFrom,
        act.PeriodTo,
        act.ExternalNumber,
        lineCount,
        act.UpdatedAt,
        act.SubmittedAt,
        ApproverOf(act.Employee).ShortName,
        act.DecidedBy,
        act.DecidedAt);
}
