using System.Globalization;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Application.Services;

/// <summary>Настройки печатных форм.</summary>
/// <param name="Organization">Наименование организации / подразделения в шапке актов (из настроек Documents:Organization).</param>
public record DocumentPrintOptions(string? Organization);

/// <summary>Строка акта для печати.</summary>
/// <param name="Usage">Куда использовано: оборудование, описание, дата.</param>
/// <param name="UnitPrice">Цена партии; null — не печатается (нет цены или не видно пользователю).</param>
public record WriteOffActLineData(int LineNo, string Sku, string ItemName, string UnitCode, decimal Quantity, string? Usage, decimal? UnitPrice = null);

/// <summary>Отметка об утверждении акта в программе.</summary>
public record ActApprovalData(string Name, string? Position, DateTimeOffset At);

/// <summary>Данные для акта на списание.</summary>
/// <param name="Period">Период сводного акта текстом («с 01.10.2026 по 31.10.2026»).</param>
/// <param name="Approval">Кто и когда утвердил; null — не утверждён (в шапке — пустые строки для подписи).</param>
/// <param name="Watermark">Надпись поперёк страницы для неутверждённого акта («ЧЕРНОВИК»); null — без неё.</param>
/// <param name="Template">Шаблон из настроек (тексты, комиссия); null — по умолчанию.</param>
public record WriteOffActData(
    string Number,
    DateTimeOffset Date,
    string? Organization,
    string EmployeeFullName,
    string? EmployeePosition,
    string? EmployeePersonnelNumber,
    string? Period,
    string? ExternalNumber,
    string? Comment,
    IReadOnlyList<WriteOffActLineData> Lines,
    ActApprovalData? Approval = null,
    string? Watermark = null,
    string? DocumentNumber = null,
    ActTemplateDto? Template = null);

/// <summary>Построение PDF (реализация — в Infrastructure).</summary>
public interface IActPdfRenderer
{
    byte[] RenderWriteOffAct(WriteOffActData data);

    /// <summary>Несколько актов одним файлом, каждый с новой страницы.</summary>
    byte[] RenderWriteOffActs(IReadOnlyList<WriteOffActData> acts);
}

public interface IActService
{
    /// <summary>Акт на списание по документу списания (PDF): по акту — сам акт, по старому документу — акт из документа.</summary>
    Task<(byte[] Pdf, string FileName)> GetWriteOffActAsync(long documentId, ActActor actor, CancellationToken ct = default);

    /// <summary>
    /// Акт на списание (PDF). Неутверждённый — с надписью «ЧЕРНОВИК».
    /// Сводный акт кладовщика: в «куда использовано» — сотрудник и отчёт; <paramref name="order"/> —
    /// порядок строк: date (по дате, по умолчанию), employee (по сотрудникам), equipment (по оборудованию).
    /// </summary>
    Task<(byte[] Pdf, string FileName)> GetActPdfAsync(int actId, ActActor actor, CancellationToken ct = default, string? order = null);

    /// <summary>Образец акта по текущему шаблону — проверить настройки.</summary>
    Task<(byte[] Pdf, string FileName)> GetSampleAsync(CancellationToken ct = default);
}

public class ActService(
    IStockService stock,
    IWriteOffActService acts,
    IEmployeeRepository employees,
    ICatalogRepository catalog,
    IActPdfRenderer renderer,
    ISettingsService settings) : IActService
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public async Task<(byte[] Pdf, string FileName)> GetActPdfAsync(int actId, ActActor actor, CancellationToken ct = default, string? order = null)
    {
        var act = await acts.GetAsync(actId, actor, ct);
        if (act.Kind == ActKind.UsageReport)
            throw new DomainException("Отчёт об использовании не печатается — в бухгалтерию идёт сводный акт кладовщика.");

        // Сводный акт: строки в выбранном порядке, номера по порядку.
        IEnumerable<ActLineDto> ordered = act.Lines;
        if (act.Kind == ActKind.Summary)
            ordered = order switch
            {
                "employee" => act.Lines.OrderBy(l => l.EmployeeName).ThenBy(l => l.LineNo),
                "equipment" => act.Lines.OrderBy(l => l.Equipment is null).ThenBy(l => l.Equipment).ThenBy(l => l.LineNo),
                _ => act.Lines.OrderBy(l => l.LineNo)
            };
        WriteOffActLineData Line(ActLineDto l, int i) => new(i + 1, l.Sku, l.ItemName, l.UnitCode, l.Quantity,
            act.Kind == ActKind.Summary ? SummaryUsage(l) : Usage(l),
            actor.SeesPrices ? l.UnitPrice : null);
        var template = await settings.GetActTemplateAsync(ct);

        string? period = (act.PeriodFrom, act.PeriodTo) switch
        {
            (DateOnly f, DateOnly t) => $"с {Day(f)} по {Day(t)}",
            (DateOnly f, null) => $"с {Day(f)}",
            (null, DateOnly t) => $"по {Day(t)}",
            _ => null
        };

        var approval = act.Status == ActStatus.Approved && act.DecidedBy is not null && act.DecidedAt is { } at
            ? new ActApprovalData(await DecidedByFullAsync(act, ct), act.DecidedByPosition, at)
            : null;
        var watermark = act.Status switch
        {
            ActStatus.Approved => null,
            ActStatus.Pending => "НА УТВЕРЖДЕНИИ",
            _ => "ЧЕРНОВИК"
        };
        var date = act.DecidedAt is { } decided && act.Status == ActStatus.Approved ? decided : act.SubmittedAt ?? act.UpdatedAt;

        // Сводный акт: отдельный акт на каждого МОЛ-кладовщика (в 1С каждый списывает своё).
        var groups = act.Kind == ActKind.Summary
            ? ordered.GroupBy(l => l.MolId).OrderBy(g => g.Key is null ? 0 : 1).ThenBy(g => g.First().Mol).ToList()
            : ordered.GroupBy(_ => (int?)null).ToList();

        var pages = new List<WriteOffActData>();
        for (var n = 0; n < groups.Count; n++)
        {
            var group = groups[n];
            var molId = group.Key ?? act.EmployeeId;   // МОЛ не указан — кладовщик, составивший акт
            var mol = await employees.GetAsync(molId, ct);
            pages.Add(new WriteOffActData(
                groups.Count > 1 ? $"{act.Number}/{n + 1}" : act.Number,
                date,
                template.Organization,
                mol?.FullName ?? (group.Key is null ? act.EmployeeName : group.First().Mol ?? act.EmployeeName),
                mol?.Position ?? (molId == act.EmployeeId ? act.EmployeePosition : null),
                mol?.PersonnelNumber,
                period,
                act.ExternalNumber,
                act.Comment,
                group.Select(Line).ToList(),
                approval,
                watermark,
                act.Kind == ActKind.Summary ? string.Join(", ", act.Documents?.Select(d => d.Number) ?? []) : act.DocumentNumber,
                template));
        }

        return (renderer.RenderWriteOffActs(pages), $"Акт_{act.Number}.pdf");
    }

    public async Task<(byte[] Pdf, string FileName)> GetWriteOffActAsync(long documentId, ActActor actor, CancellationToken ct = default)
    {
        var doc = await stock.GetDocumentAsync(documentId, ct);
        if (doc.Type != MovementType.WriteOff)
            throw new DomainException($"Документ {doc.Number} не является списанием — акт не формируется.");

        // Списание по акту — печатается сам акт (со строками «куда использовано» и отметкой утверждения).
        if (doc.ActId is int actId)
            return await GetActPdfAsync(actId, actor, ct);

        // Списания, проведённые до появления актов: «куда использовано» — общее для всех строк.
        var employee = doc.ReceivedById is int employeeId ? await employees.GetAsync(employeeId, ct) : null;

        string? equipment = null;
        if (doc.EquipmentId is int equipmentId)
        {
            var found = (await catalog.ListEquipmentAsync(ct)).FirstOrDefault(e => e.Id == equipmentId);
            equipment = found is null ? null : $"{found.Code} — {found.Name}";
        }
        var usage = string.Join("; ", new[] { equipment, doc.Comment }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var template = await settings.GetActTemplateAsync(ct);

        var data = new WriteOffActData(
            doc.Number,
            doc.OccurredAt,
            template.Organization,
            employee?.FullName ?? doc.ReceivedBy ?? "—",
            employee?.Position,
            employee?.PersonnelNumber,
            null,
            doc.ExternalNumber,
            null,
            doc.Lines
                .Select(l => new WriteOffActLineData(l.LineNo, l.Sku, l.ItemName, l.UnitCode, l.Quantity, usage.Length == 0 ? null : usage,
                    actor.SeesPrices ? l.UnitPrice : null))
                .ToList(),
            Template: template);

        return (renderer.RenderWriteOffAct(data), $"Акт_{doc.Number}.pdf");
    }

    public async Task<(byte[] Pdf, string FileName)> GetSampleAsync(CancellationToken ct = default)
    {
        var template = await settings.GetActTemplateAsync(ct);
        var data = new WriteOffActData(
            "АС-000000",
            DateTimeOffset.Now,
            template.Organization,
            "Иванов Иван Иванович",
            "Слесарь-ремонтник",
            "0001",
            null,
            "Наряд № 1",
            null,
            [
                new WriteOffActLineData(1, "ЗЧ-000001", "Подшипник 6205-2RS", "шт", 2, "Насос центробежный; замена подшипников", 450m),
                new WriteOffActLineData(2, "ЗЧ-000002", "Ремень приводной A-1250", "шт", 1, "Транспортёр ящиков", 820m)
            ],
            Watermark: "ОБРАЗЕЦ",
            Template: template);

        return (renderer.RenderWriteOffAct(data), "Образец_акта.pdf");
    }

    /// <summary>Утвердивший — полным ФИО (в акте хранится краткое «Иванов И. П.»).</summary>
    private async Task<string> DecidedByFullAsync(ActDto act, CancellationToken ct) =>
        act.DecidedById is int id && await employees.GetAsync(id, ct) is { } e ? e.FullName : act.DecidedBy!;

    private static string Day(DateOnly d) => d.ToString("dd.MM.yyyy", Ru);

    private static string? Usage(ActLineDto line)
    {
        var parts = new[] { line.Equipment, line.Usage, line.UsedOn is DateOnly d ? Day(d) : null }
            .Where(s => !string.IsNullOrWhiteSpace(s));
        var text = string.Join("; ", parts);
        return text.Length == 0 ? null : text;
    }

    /// <summary>Строка сводного акта: «Петров С. (ОИ-000012): Насос Н-3; замена подшипника; 15.10.2026».</summary>
    private static string? SummaryUsage(ActLineDto line)
    {
        var who = line.EmployeeName is null ? null : line.SourceNumber is null ? line.EmployeeName : $"{line.EmployeeName} ({line.SourceNumber})";
        var usage = Usage(line);
        return who is null ? usage : usage is null ? who : $"{who}: {usage}";
    }
}
