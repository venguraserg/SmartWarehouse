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
public record WriteOffActLineData(int LineNo, string Sku, string ItemName, string UnitCode, decimal Quantity, string? Usage);

/// <summary>Отметка об утверждении акта в программе.</summary>
public record ActApprovalData(string Name, string? Position, DateTimeOffset At);

/// <summary>Данные для акта на списание.</summary>
/// <param name="Period">Период сводного акта текстом («с 01.10.2026 по 31.10.2026»).</param>
/// <param name="Approval">Кто и когда утвердил; null — не утверждён (в шапке — пустые строки для подписи).</param>
/// <param name="Watermark">Надпись поперёк страницы для неутверждённого акта («ЧЕРНОВИК»); null — без неё.</param>
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
    string? DocumentNumber = null);

/// <summary>Построение PDF (реализация — в Infrastructure).</summary>
public interface IActPdfRenderer
{
    byte[] RenderWriteOffAct(WriteOffActData data);
}

public interface IActService
{
    /// <summary>Акт на списание по документу списания (PDF): по акту — сам акт, по старому документу — акт из документа.</summary>
    Task<(byte[] Pdf, string FileName)> GetWriteOffActAsync(long documentId, ActActor actor, CancellationToken ct = default);

    /// <summary>Акт на списание (PDF). Неутверждённый — с надписью «ЧЕРНОВИК».</summary>
    Task<(byte[] Pdf, string FileName)> GetActPdfAsync(int actId, ActActor actor, CancellationToken ct = default);
}

public class ActService(
    IStockService stock,
    IWriteOffActService acts,
    IEmployeeRepository employees,
    ICatalogRepository catalog,
    IActPdfRenderer renderer,
    DocumentPrintOptions options) : IActService
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public async Task<(byte[] Pdf, string FileName)> GetActPdfAsync(int actId, ActActor actor, CancellationToken ct = default)
    {
        var act = await acts.GetAsync(actId, actor, ct);
        var employee = await employees.GetAsync(act.EmployeeId, ct);

        string? period = (act.PeriodFrom, act.PeriodTo) switch
        {
            (DateOnly f, DateOnly t) => $"с {Day(f)} по {Day(t)}",
            (DateOnly f, null) => $"с {Day(f)}",
            (null, DateOnly t) => $"по {Day(t)}",
            _ => null
        };

        var data = new WriteOffActData(
            act.Number,
            act.DecidedAt is { } decided && act.Status == ActStatus.Approved ? decided : act.SubmittedAt ?? act.UpdatedAt,
            Organization(),
            employee?.FullName ?? act.EmployeeName,
            employee?.Position ?? act.EmployeePosition,
            employee?.PersonnelNumber,
            period,
            act.ExternalNumber,
            act.Comment,
            act.Lines
                .Select(l => new WriteOffActLineData(l.LineNo, l.Sku, l.ItemName, l.UnitCode, l.Quantity, Usage(l)))
                .ToList(),
            act.Status == ActStatus.Approved && act.DecidedBy is not null && act.DecidedAt is { } at
                ? new ActApprovalData(await DecidedByFullAsync(act, ct), act.DecidedByPosition, at)
                : null,
            act.Status switch
            {
                ActStatus.Approved => null,
                ActStatus.Pending => "НА УТВЕРЖДЕНИИ",
                _ => "ЧЕРНОВИК"
            },
            act.DocumentNumber);

        return (renderer.RenderWriteOffAct(data), $"Акт_{act.Number}.pdf");
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

        var data = new WriteOffActData(
            doc.Number,
            doc.OccurredAt,
            Organization(),
            employee?.FullName ?? doc.ReceivedBy ?? "—",
            employee?.Position,
            employee?.PersonnelNumber,
            null,
            doc.ExternalNumber,
            null,
            doc.Lines
                .Select(l => new WriteOffActLineData(l.LineNo, l.Sku, l.ItemName, l.UnitCode, l.Quantity, usage.Length == 0 ? null : usage))
                .ToList());

        return (renderer.RenderWriteOffAct(data), $"Акт_{doc.Number}.pdf");
    }

    private string? Organization() =>
        string.IsNullOrWhiteSpace(options.Organization) ? null : options.Organization.Trim();

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
}
