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
/// <param name="SeesPrices">Видит цены и суммы (все, кроме рядовых сотрудников).</param>
public record ActActor(int? EmployeeId, string? DisplayName, bool IsWarehouse, bool IsManager, bool SeesPrices = true);

public interface IWriteOffActService
{
    Task<IReadOnlyList<ActSummaryDto>> ListAsync(ActScope scope, ActStatus? status, int? employeeId, ActActor actor, CancellationToken ct = default,
        ActKind? kind = null);

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

    // ---------- Сводный акт кладовщика (СА) ----------

    /// <summary>Отчёты, которые можно включить в сводный акт (и уже включённые в <paramref name="summaryId"/>).</summary>
    Task<IReadOnlyList<ActSummaryDto>> ListSummaryCandidatesAsync(int? summaryId, ActActor actor, CancellationToken ct = default);

    /// <summary>Новый сводный акт (черновик) из подтверждённых отчётов об использовании.</summary>
    Task<ActDto> CreateSummaryAsync(SaveSummaryRequest request, ActActor actor, CancellationToken ct = default);

    Task<ActDto> UpdateSummaryAsync(int id, SaveSummaryRequest request, ActActor actor, CancellationToken ct = default);

    /// <summary>Провести: учётные строки включённых отчётов списываются (документ на каждого сотрудника).</summary>
    Task<ActDto> PostSummaryAsync(int id, ActActor actor, CancellationToken ct = default);

    /// <summary>Вписать номер документа списания в 1С (в том числе после проведения).</summary>
    Task<ActDto> SetExternalNumberAsync(int id, string? externalNumber, ActActor actor, CancellationToken ct = default);

    /// <summary>Сколько ждут решения текущего пользователя: акты подчинённых, а кладовщику — и отчёты на подтверждении.</summary>
    Task<int> CountToApproveAsync(ActActor actor, CancellationToken ct = default);
}

/// <summary>
/// Акты на списание и отчёты об использовании — один механизм, разные правила (<see cref="ActKind"/>).
/// <list type="bullet">
/// <item>Акт (АС): «подотчёт по 1С», утверждает руководитель — проводится списание с сотрудника.</item>
/// <item>Отчёт (ОИ): выданное «в работу», подтверждает кладовщик (внесённый кладовщиком — подтверждён сразу).
/// Подтверждённое остаётся в резерве до сводного акта кладовщика за месяц; вне учёта списывается сразу.</item>
/// </list>
/// Остатки меняются только проведением документа списания (через <see cref="StockService"/>),
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
        ActScope scope, ActStatus? status, int? employeeId, ActActor actor, CancellationToken ct = default, ActKind? kind = null)
    {
        ActFilter filter;
        if (scope == ActScope.ToApprove)
        {
            if (actor.EmployeeId is null && !actor.IsWarehouse)
                return [];
            filter = new ActFilter(null, actor.EmployeeId, ActStatus.Pending, employeeId, 500, actor.IsWarehouse, kind);
        }
        else
        {
            filter = new ActFilter(await VisibleEmployeesAsync(actor, ct), null, status, employeeId, 500, Kind: kind);
        }

        var headers = await acts.ListAsync(filter, ct);
        return headers
            .Where(h => actor.IsWarehouse || h.Act.Kind != ActKind.Summary)   // сводные акты — только кладовщику
            .Select(h => ToSummary(h.Act, h.LineCount))
            .ToList();
    }

    public async Task<ActDto> GetAsync(int id, ActActor actor, CancellationToken ct = default)
    {
        var act = await LoadVisibleAsync(id, actor, ct);
        return ToDto(act, actor);
    }

    public async Task<int> CountToApproveAsync(ActActor actor, CancellationToken ct = default) =>
        actor.EmployeeId is not null || actor.IsWarehouse ? await acts.CountToApproveAsync(actor.EmployeeId, actor.IsWarehouse, ct) : 0;

    // ---------- Черновик ----------

    public async Task<ActDto> CreateAsync(SaveActRequest request, ActActor actor, CancellationToken ct = default)
    {
        if (request.Kind == ActKind.Summary)
            throw new DomainException("Сводный акт кладовщика составляется из отчётов об использовании.");
        if (!actor.IsWarehouse && actor.EmployeeId != request.EmployeeId)
            throw new ForbiddenException(request.Kind == ActKind.UsageReport
                ? "Отчёт об использовании вносит сам сотрудник (или кладовщик с его слов)."
                : "Акт на списание составляет сам сотрудник (или кладовщик за него).");

        var employee = await employees.GetAsync(request.EmployeeId, ct)
            ?? throw new NotFoundException($"Сотрудник {request.EmployeeId} не найден.");

        var now = clock.GetUtcNow();
        var act = new WriteOffAct
        {
            EmployeeId = employee.Id,
            Kind = request.Kind,
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
        if (request.Kind != act.Kind || act.Kind == ActKind.Summary)
            throw new DomainException("Акт нельзя превратить в отчёт об использовании и наоборот — составьте новый.");

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
        // Черновик сводного акта: включённые отчёты снова свободны.
        foreach (var report in act.Reports)
        {
            report.SummaryActId = null;
            report.SummaryAct = null;
        }
        acts.Remove(act);
        await acts.SaveChangesAsync(ct);
    }

    // ---------- Утверждение ----------

    public async Task<ActDto> SubmitAsync(int id, ActActor actor, CancellationToken ct = default)
    {
        var act = await LoadVisibleAsync(id, actor, ct);
        EnsureNotSummary(act);
        EnsureCanEdit(act, actor);
        if (act.Lines.Count == 0)
            throw new DomainException("В акте нет строк.");

        // За время черновика подотчёт мог измениться (возврат, другой акт) — проверяем снова
        // и закрепляем партии: на утверждении резервируются именно они.
        var current = act.Lines.OrderBy(l => l.LineNo)
            .Select(l => new ActLineRequest(l.ItemId, l.Quantity, l.EquipmentId, l.Usage, l.UsedOn, l.BatchId))
            .ToList();
        var allocated = await AllocateAsync(act.Employee, act.Custody, current, act.Lines.GroupBy(l => l.ItemId).ToDictionary(g => g.Key, g => g.First().Item), act.Id, ct);
        SetLines(act, allocated);

        var now = clock.GetUtcNow();
        act.Status = ActStatus.Pending;
        act.SubmittedAt = now;
        act.UpdatedAt = now;

        // Отчёт, который вносит кладовщик (со слов сотрудника), подтверждён сразу.
        // Акт: руководителя нет — сотрудник утверждает свои акты сам, без лишнего шага.
        if (act.Kind == ActKind.UsageReport ? actor.IsWarehouse
                : ApproverOf(act.Employee).Id == act.EmployeeId && actor.EmployeeId == act.EmployeeId)
            await ApproveCoreAsync(act, actor, ct);

        await acts.SaveChangesAsync(ct);
        return await GetAsync(act.Id, actor, ct);
    }

    public async Task<ActDto> RecallAsync(int id, ActActor actor, CancellationToken ct = default)
    {
        var act = await LoadVisibleAsync(id, actor, ct);
        EnsureNotSummary(act);
        if (!CanRecall(act, actor))
            throw act.Status == ActStatus.Pending
                ? new ForbiddenException("Отозвать может сам сотрудник или кладовщик.")
                : new DomainException("Отозвать можно только то, что на утверждении (подтверждении).");

        act.Status = ActStatus.Draft;
        act.UpdatedAt = clock.GetUtcNow();
        await acts.SaveChangesAsync(ct);
        return await GetAsync(act.Id, actor, ct);
    }

    public async Task<ActDto> ApproveAsync(int id, ActActor actor, CancellationToken ct = default)
    {
        var act = await LoadVisibleAsync(id, actor, ct);
        EnsureNotSummary(act);
        EnsureCanDecide(act, actor);

        await ApproveCoreAsync(act, actor, ct);
        await acts.SaveChangesAsync(ct);
        return await GetAsync(act.Id, actor, ct);
    }

    public async Task<ActDto> ReturnAsync(int id, string? remark, ActActor actor, CancellationToken ct = default)
    {
        var act = await LoadVisibleAsync(id, actor, ct);
        EnsureNotSummary(act);
        EnsureCanDecide(act, actor);

        var text = Clean(remark) ?? throw new DomainException("Напишите замечание: что нужно исправить.");
        if (text.Length > 1000)
            throw new DomainException("Замечание — не длиннее 1000 символов.");

        var now = clock.GetUtcNow();
        act.Status = ActStatus.Returned;
        act.ReturnRemark = text;
        SetDecider(act, actor);
        act.DecidedAt = now;
        act.UpdatedAt = now;

        await acts.SaveChangesAsync(ct);
        return await GetAsync(act.Id, actor, ct);
    }

    /// <summary>
    /// Утверждает акт или подтверждает отчёт (без сохранения). Акт — проводится списание всего с «подотчёта по 1С».
    /// Отчёт — списываются сразу только партии вне учёта (с «в работе»); остальное остаётся в резерве
    /// до сводного акта кладовщика.
    /// </summary>
    private async Task ApproveCoreAsync(WriteOffAct act, ActActor actor, CancellationToken ct)
    {
        var lines = act.Lines.OrderBy(l => l.LineNo).ToList();
        if (act.Kind == ActKind.UsageReport)
        {
            // Партии строк только что закреплены (при отправке) — навигация может быть не загружена.
            var offBook = new List<WriteOffActLine>();
            foreach (var l in lines)
                if ((l.Batch ?? (l.BatchId is int b ? await stockRepo.GetBatchAsync(b, ct) : null))?.OffBook == true)
                    offBook.Add(l);
            lines = offBook;
        }

        if (lines.Count > 0)
        {
            // Если всё израсходовано на одно оборудование — оно же попадает в документ.
            var equipment = lines.Select(l => l.EquipmentId).Distinct().ToList();
            var comment = act.Kind == ActKind.UsageReport
                ? $"Вне учёта, по отчёту об использовании {act.Number}"
                : $"По акту на списание {act.Number}";
            if (act.ExternalNumber is not null)
                comment += $" ({act.ExternalNumber})";

            var request = new CreateDocumentRequest(
                MovementType.WriteOff,
                act.Number,
                equipment.Count == 1 ? equipment[0] : null,
                null,
                comment,
                lines.Select(l => new DocumentLineRequest(l.ItemId, l.Quantity, null, null, l.BatchId)).ToList(),
                PerformedById: act.Kind == ActKind.UsageReport ? actor.EmployeeId : ApproverOf(act.Employee).Id,
                ReceivedById: act.EmployeeId,
                Custody: act.Custody);

            var staged = await stock.StageAsync(request, exceptActId: act.Id, ct);
            act.Document = staged.Document;
            staged.Document.Act = act;
            if (act.Kind == ActKind.UsageReport)
                foreach (var l in lines)
                    l.Document = staged.Document;
        }

        var now = clock.GetUtcNow();
        act.Status = ActStatus.Approved;
        SetDecider(act, actor);
        act.DecidedAt = now;
        act.UpdatedAt = now;
        act.ReturnRemark = null;
    }

    /// <summary>Кто решил: по акту — руководитель сотрудника, по отчёту — кладовщик, который подтвердил (вернул).</summary>
    private static void SetDecider(WriteOffAct act, ActActor actor)
    {
        if (act.Kind == ActKind.UsageReport)
        {
            act.DecidedById = actor.EmployeeId;
            act.DecidedBy = Clean(actor.DisplayName);
            act.DecidedByPosition = null;
            return;
        }
        var approver = ApproverOf(act.Employee);
        act.DecidedById = approver.Id;
        act.DecidedBy = approver.ShortName;
        act.DecidedByPosition = approver.Position;
    }

    // ---------- Сводный акт кладовщика ----------

    public const int MaxSummaryReports = 500;

    public async Task<IReadOnlyList<ActSummaryDto>> ListSummaryCandidatesAsync(int? summaryId, ActActor actor, CancellationToken ct = default)
    {
        EnsureWarehouse(actor);
        var reports = await acts.ListSummaryCandidatesAsync(summaryId, ct);
        return reports.Select(r => ToSummary(r, PendingLines(r).Count())).ToList();
    }

    public async Task<ActDto> CreateSummaryAsync(SaveSummaryRequest request, ActActor actor, CancellationToken ct = default)
    {
        EnsureWarehouse(actor);
        if (actor.EmployeeId is not int keeper)
            throw new DomainException("Учётная запись кладовщика не привязана к сотруднику — сводный акт составляется от его имени (МОЛ).");

        var now = clock.GetUtcNow();
        var act = new WriteOffAct
        {
            Kind = ActKind.Summary,
            EmployeeId = keeper,
            Status = ActStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = Clean(actor.DisplayName)
        };
        acts.Add(act);
        await ApplySummaryAsync(act, request, ct);

        await acts.SaveChangesAsync(ct);
        return await GetAsync(act.Id, actor, ct);
    }

    public async Task<ActDto> UpdateSummaryAsync(int id, SaveSummaryRequest request, ActActor actor, CancellationToken ct = default)
    {
        var act = await LoadVisibleAsync(id, actor, ct);
        EnsureSummary(act);
        EnsureCanEdit(act, actor);

        await ApplySummaryAsync(act, request, ct);
        act.UpdatedAt = clock.GetUtcNow();
        await acts.SaveChangesAsync(ct);
        return await GetAsync(act.Id, actor, ct);
    }

    public async Task<ActDto> PostSummaryAsync(int id, ActActor actor, CancellationToken ct = default)
    {
        var act = await LoadVisibleAsync(id, actor, ct);
        EnsureSummary(act);
        EnsureCanEdit(act, actor);

        var pending = act.Reports
            .SelectMany(r => PendingLines(r).Select(l => (Report: r, Line: l)))
            .ToList();
        if (pending.Count == 0)
            throw new DomainException("В сводном акте нет строк к списанию — включите подтверждённые отчёты.");

        // Документ списания на каждого сотрудника: позиции списываются с его «в работе»
        // (резерв отчётов этого сводного акта не мешает — exceptActId).
        foreach (var group in pending.GroupBy(x => x.Report.EmployeeId))
        {
            var reportNumbers = string.Join(", ", group.Select(x => x.Report.Number).Distinct());
            foreach (var chunk in group.Chunk(StockService.MaxLines))
            {
                var equipment = chunk.Select(x => x.Line.EquipmentId).Distinct().ToList();
                var request = new CreateDocumentRequest(
                    MovementType.WriteOff,
                    act.Number,
                    equipment.Count == 1 ? equipment[0] : null,
                    null,
                    $"По сводному акту {act.Number} (отчёты {reportNumbers})"
                        + (act.ExternalNumber is null ? "" : $"; в 1С: {act.ExternalNumber}"),
                    chunk.Select(x => new DocumentLineRequest(x.Line.ItemId, x.Line.Quantity, null, null, x.Line.BatchId)).ToList(),
                    PerformedById: act.EmployeeId,
                    ReceivedById: group.Key,
                    Custody: CustodyKind.Work);

                var staged = await stock.StageAsync(request, exceptActId: act.Id, ct);
                foreach (var (_, line) in chunk)
                    line.Document = staged.Document;
            }
        }

        var now = clock.GetUtcNow();
        act.Status = ActStatus.Approved;
        act.DecidedById = actor.EmployeeId;
        act.DecidedBy = Clean(actor.DisplayName);
        act.DecidedAt = now;
        act.UpdatedAt = now;

        await acts.SaveChangesAsync(ct);
        return await GetAsync(act.Id, actor, ct);
    }

    public async Task<ActDto> SetExternalNumberAsync(int id, string? externalNumber, ActActor actor, CancellationToken ct = default)
    {
        var act = await LoadVisibleAsync(id, actor, ct);
        EnsureSummary(act);
        EnsureWarehouse(actor);

        var number = Clean(externalNumber);
        if (number?.Length > 100)
            throw new DomainException("Номер документа 1С — не длиннее 100 символов.");
        act.ExternalNumber = number;
        act.UpdatedAt = clock.GetUtcNow();
        await acts.SaveChangesAsync(ct);
        return await GetAsync(act.Id, actor, ct);
    }

    /// <summary>Реквизиты и состав сводного акта: только подтверждённые отчёты с несписанными учётными строками.</summary>
    private async Task ApplySummaryAsync(WriteOffAct act, SaveSummaryRequest request, CancellationToken ct)
    {
        var ids = (request.ReportIds ?? Array.Empty<int>()).Distinct().ToList();
        if (ids.Count == 0)
            throw new DomainException("Отметьте отчёты об использовании, которые включить в акт.");
        if (ids.Count > MaxSummaryReports)
            throw new DomainException($"Не больше {MaxSummaryReports} отчётов в одном сводном акте.");
        if (request.PeriodFrom is DateOnly from && request.PeriodTo is DateOnly to && from > to)
            throw new DomainException("Начало периода позже его окончания.");

        var externalNumber = Clean(request.ExternalNumber);
        if (externalNumber?.Length > 100)
            throw new DomainException("Номер документа 1С — не длиннее 100 символов.");
        var comment = Clean(request.Comment);
        if (comment?.Length > 1000)
            throw new DomainException("Комментарий — не длиннее 1000 символов.");

        var reports = await acts.GetManyAsync(ids, ct);
        foreach (var id in ids)
        {
            var report = reports.FirstOrDefault(r => r.Id == id)
                ?? throw new NotFoundException($"Отчёт {WriteOffAct.FormatNumber(id, ActKind.UsageReport)} не найден.");
            if (report.Kind != ActKind.UsageReport || report.Status != ActStatus.Approved)
                throw new DomainException($"{report.Number}: в сводный акт включаются только подтверждённые отчёты об использовании.");
            if (report.SummaryActId is int other && other != act.Id)
                throw new DomainException($"{report.Number} уже включён в сводный акт {WriteOffAct.FormatNumber(other, ActKind.Summary)}.");
            if (!PendingLines(report).Any())
                throw new DomainException($"{report.Number}: учётные позиции уже списаны.");
        }

        // Исключённые из черновика отчёты снова свободны; включённые — привязываются.
        foreach (var report in act.Reports.Where(r => !ids.Contains(r.Id)).ToList())
        {
            report.SummaryActId = null;
            report.SummaryAct = null;
            act.Reports.Remove(report);
        }
        foreach (var report in reports.Where(r => act.Reports.All(x => x.Id != r.Id)))
        {
            report.SummaryAct = act;
            act.Reports.Add(report);
        }

        act.PeriodFrom = request.PeriodFrom;
        act.PeriodTo = request.PeriodTo;
        act.ExternalNumber = externalNumber;
        act.Comment = comment;
    }

    /// <summary>Строки отчёта, которые списывает сводный акт: учётные (не вне учёта) и ещё не списанные.</summary>
    private static IEnumerable<WriteOffActLine> PendingLines(WriteOffAct report) =>
        report.Lines.Where(l => l.DocumentId is null && l.Document is null && l.Batch?.OffBook != true);

    private static void EnsureWarehouse(ActActor actor)
    {
        if (!actor.IsWarehouse)
            throw new ForbiddenException("Сводный акт составляет кладовщик.");
    }

    private static void EnsureSummary(WriteOffAct act)
    {
        if (act.Kind != ActKind.Summary)
            throw new DomainException($"{act.Number} — не сводный акт кладовщика.");
    }

    private static void EnsureNotSummary(WriteOffAct act)
    {
        if (act.Kind == ActKind.Summary)
            throw new DomainException($"Сводный акт {act.Number} не утверждается, а проводится кладовщиком.");
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

        var allocated = await AllocateAsync(employee, act.Custody, lines, items, act.Id == 0 ? null : act.Id, ct);

        act.PeriodFrom = request.PeriodFrom;
        act.PeriodTo = request.PeriodTo;
        act.ExternalNumber = externalNumber;
        act.Comment = comment;
        SetLines(act, allocated);
    }

    private static void SetLines(WriteOffAct act, List<(ActLineRequest Line, int BatchId, decimal Quantity)> allocated)
    {
        act.Lines.Clear();
        var lineNo = 0;
        foreach (var (line, batchId, quantity) in allocated)
        {
            act.Lines.Add(new WriteOffActLine
            {
                LineNo = ++lineNo,
                ItemId = line.ItemId,
                BatchId = batchId,
                Quantity = quantity,
                EquipmentId = line.EquipmentId,
                Usage = Clean(line.Usage),
                UsedOn = line.UsedOn
            });
        }
    }

    /// <summary>
    /// Распределяет строки по партиям подотчёта нужного вида (акт — «по 1С», отчёт — «в работе»):
    /// указанная партия или самая старая (при нехватке строка делится на несколько партий).
    /// Учитывает резерв других актов и отчётов.
    /// </summary>
    private async Task<List<(ActLineRequest Line, int BatchId, decimal Quantity)>> AllocateAsync(
        Employee employee, CustodyKind custody, IReadOnlyList<ActLineRequest> lines, IReadOnlyDictionary<int, Item> items, int? exceptActId, CancellationToken ct)
    {
        var free = new Dictionary<int, Dictionary<int, decimal>>();      // позиция → партия → свободно
        var order = new Dictionary<int, List<int>>();                     // позиция → партии от старой к новой
        var held = new Dictionary<int, decimal>();                        // позиция → зарезервировано другими актами

        foreach (var itemId in lines.Select(l => l.ItemId).Distinct())
        {
            var batches = (await stockRepo.GetEmployeeBatchesAsync(itemId, employee.Id, ct)).Where(b => b.Kind == custody).ToList();
            var reserved = await stockRepo.GetReservedByBatchAsync(itemId, employee.Id, exceptActId, ct);
            free[itemId] = batches.ToDictionary(b => b.BatchId, b => Math.Max(0, b.Quantity - reserved.GetValueOrDefault((b.BatchId, custody))));
            order[itemId] = batches.OrderBy(b => b.Batch.ReceivedAt).ThenBy(b => b.BatchId).Select(b => b.BatchId).ToList();
            held[itemId] = reserved.Where(kv => kv.Key.Kind == custody).Sum(kv => kv.Value);
        }

        var result = new List<(ActLineRequest, int, decimal)>();
        foreach (var line in lines)
        {
            var item = items[line.ItemId];
            var available = free[line.ItemId];
            if (line.BatchId is int chosen)
            {
                var have = available.GetValueOrDefault(chosen);
                if (!available.ContainsKey(chosen) || have < line.Quantity)
                    throw new InsufficientStockException(item.Sku, employee.ShortName, have, line.Quantity, isEmployee: true, reserved: held[line.ItemId]);
                available[chosen] = have - line.Quantity;
                result.Add((line, chosen, line.Quantity));
                continue;
            }

            var total = available.Values.Sum();
            if (total < line.Quantity)
                throw new InsufficientStockException(item.Sku, employee.ShortName, total, line.Quantity, isEmployee: true, reserved: held[line.ItemId]);

            var left = line.Quantity;
            foreach (var batchId in order[line.ItemId])
            {
                var take = Math.Min(available[batchId], left);
                if (take <= 0)
                    continue;
                available[batchId] -= take;
                left -= take;
                result.Add((line, batchId, take));
                if (left == 0)
                    break;
            }
        }
        return result;
    }


    private async Task<WriteOffAct> LoadVisibleAsync(int id, ActActor actor, CancellationToken ct)
    {
        var act = await acts.GetAsync(id, ct) ?? throw new NotFoundException($"Акт {id} не найден.");
        if (actor.IsWarehouse || (act.Kind == ActKind.WriteOff && actor.EmployeeId == ApproverOf(act.Employee).Id))
            return act;
        if (act.Kind == ActKind.Summary)
            throw new ForbiddenException("Сводные акты видит кладовщик.");

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
        act.IsEditable && (actor.IsWarehouse || (act.Kind != ActKind.Summary && actor.EmployeeId == act.EmployeeId));

    private static bool CanRecall(WriteOffAct act, ActActor actor) =>
        act.Kind != ActKind.Summary && act.Status == ActStatus.Pending && (actor.IsWarehouse || actor.EmployeeId == act.EmployeeId);

    /// <summary>Акт утверждает руководитель сотрудника; отчёт подтверждает кладовщик.</summary>
    private static bool CanDecide(WriteOffAct act, ActActor actor) =>
        act.Kind != ActKind.Summary && act.Status == ActStatus.Pending && (act.Kind == ActKind.UsageReport
            ? actor.IsWarehouse
            : actor.EmployeeId is int me && me == ApproverOf(act.Employee).Id);

    private static void EnsureCanEdit(WriteOffAct act, ActActor actor)
    {
        if (!act.IsEditable)
            throw new DomainException(act.Status == ActStatus.Pending
                ? $"{act.Number} на утверждении (подтверждении) — сначала отзовите."
                : $"{act.Number} уже утверждён (подтверждён), изменить нельзя.");
        if (!CanEdit(act, actor))
            throw new ForbiddenException("Изменять акт может сам сотрудник или кладовщик.");
    }

    private static void EnsureCanDecide(WriteOffAct act, ActActor actor)
    {
        if (act.Status != ActStatus.Pending)
            throw new DomainException(act.Kind == ActKind.UsageReport
                ? $"Отчёт {act.Number} не на подтверждении."
                : $"Акт {act.Number} не на утверждении.");
        if (!CanDecide(act, actor))
            throw new ForbiddenException(act.Kind == ActKind.UsageReport
                ? "Отчёт об использовании подтверждает кладовщик."
                : $"Акт утверждает {ApproverOf(act.Employee).ShortName} — непосредственный руководитель сотрудника.");
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // ---------- Преобразование ----------

    private static ActDto ToDto(WriteOffAct act, ActActor actor)
    {
        var approver = DeciderOf(act);

        // Сводный акт: строки — учётные строки включённых отчётов (по дате использования), с сотрудником и отчётом.
        var lines = act.Kind == ActKind.Summary
            ? act.Reports
                .SelectMany(r => r.Lines.Where(l => l.Batch?.OffBook != true).Select(l => (Report: r, Line: l)))
                .OrderBy(x => x.Line.UsedOn ?? DateOnly.FromDateTime((x.Report.DecidedAt ?? x.Report.UpdatedAt).UtcDateTime))
                .ThenBy(x => x.Report.Id).ThenBy(x => x.Line.LineNo)
                .Select((x, i) => LineDto(x.Line, i + 1, x.Report))
                .ToList()
            : act.Lines.OrderBy(l => l.LineNo).Select(l => LineDto(l, l.LineNo, null)).ToList();
        var documents = (act.Kind == ActKind.Summary ? act.Reports.SelectMany(r => r.Lines) : act.Lines)
            .Where(l => l.DocumentId is not null || l.Document is not null)
            .Select(l => l.DocumentId ?? l.Document!.Id)
            .Distinct()
            .Select(id => new DocumentRefDto(id, StockDocument.FormatNumber(MovementType.WriteOff, id)))
            .ToList();
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
            approver.Name,
            act.DecidedById,
            act.DecidedBy,
            act.DecidedByPosition,
            act.DecidedAt,
            act.ReturnRemark,
            act.DocumentId,
            act.DocumentId is long documentId ? StockDocument.FormatNumber(MovementType.WriteOff, documentId) : null,
            lines,
            new ActPermissions(CanEdit(act, actor), CanEdit(act, actor), CanRecall(act, actor), CanDecide(act, actor)),
            act.Kind,
            act.Kind == ActKind.Summary ? act.Reports.OrderBy(r => r.Id).Select(r => ToSummary(r, r.Lines.Count(l => l.Batch?.OffBook != true))).ToList() : null,
            documents,
            act.SummaryActId,
            act.SummaryActId is int s ? WriteOffAct.FormatNumber(s, ActKind.Summary) : null,
            act.SummaryAct?.Status == ActStatus.Approved);
    }

    private static ActLineDto LineDto(WriteOffActLine l, int lineNo, WriteOffAct? report) => new(
        lineNo, l.ItemId, l.Item.Sku, l.Item.Name, l.Item.Unit.Code, l.Quantity,
        l.EquipmentId, l.Equipment is { } e ? $"{e.Code} — {e.Name}" : null,
        l.Usage, l.UsedOn, l.BatchId, l.Batch?.UnitPrice, l.Batch?.ReceivedAt,
        EmployeeName: report?.Employee.ShortName,
        SourceNumber: report?.Number,
        SourceId: report?.Id);

    /// <summary>Кто решает: по акту — руководитель (или сам сотрудник), по отчёту — кладовщик.</summary>
    private static (int Id, string Name) DeciderOf(WriteOffAct act)
    {
        if (act.Kind != ActKind.WriteOff)
            return (0, "кладовщик");
        var approver = ApproverOf(act.Employee);
        return (approver.Id, approver.ShortName);
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
        DeciderOf(act).Name,
        act.DecidedBy,
        act.DecidedAt,
        act.Kind,
        act.SummaryActId is int s ? WriteOffAct.FormatNumber(s, ActKind.Summary) : null,
        act.SummaryAct?.Status == ActStatus.Approved);
}
