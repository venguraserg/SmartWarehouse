using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Application.Services;

/// <summary>Настройки печатных форм.</summary>
/// <param name="Organization">Наименование организации / подразделения в шапке актов (из настроек Documents:Organization).</param>
public record DocumentPrintOptions(string? Organization);

/// <summary>Данные для акта на списание.</summary>
public record WriteOffActData(
    string Number,
    DateTimeOffset Date,
    string? Organization,
    string EmployeeFullName,
    string? EmployeePosition,
    string? EmployeePersonnelNumber,
    string? Equipment,
    string? ExternalNumber,
    string? Comment,
    string? PerformedBy,
    IReadOnlyList<DocumentLineDto> Lines);

/// <summary>Построение PDF (реализация — в Infrastructure).</summary>
public interface IActPdfRenderer
{
    byte[] RenderWriteOffAct(WriteOffActData data);
}

public interface IActService
{
    /// <summary>Акт на списание по документу списания (PDF).</summary>
    Task<(byte[] Pdf, string FileName)> GetWriteOffActAsync(long documentId, CancellationToken ct = default);
}

public class ActService(
    IStockService stock,
    IEmployeeRepository employees,
    ICatalogRepository catalog,
    IActPdfRenderer renderer,
    DocumentPrintOptions options) : IActService
{
    public async Task<(byte[] Pdf, string FileName)> GetWriteOffActAsync(long documentId, CancellationToken ct = default)
    {
        var doc = await stock.GetDocumentAsync(documentId, ct);
        if (doc.Type != MovementType.WriteOff)
            throw new DomainException($"Документ {doc.Number} не является списанием — акт не формируется.");

        var employee = doc.ReceivedById is int employeeId ? await employees.GetAsync(employeeId, ct) : null;

        string? equipment = null;
        if (doc.EquipmentId is int equipmentId)
        {
            var found = (await catalog.ListEquipmentAsync(ct)).FirstOrDefault(e => e.Id == equipmentId);
            equipment = found is null ? null : $"{found.Code} — {found.Name}";
        }

        var data = new WriteOffActData(
            doc.Number,
            doc.OccurredAt,
            string.IsNullOrWhiteSpace(options.Organization) ? null : options.Organization.Trim(),
            employee?.FullName ?? doc.ReceivedBy ?? "—",
            employee?.Position,
            employee?.PersonnelNumber,
            equipment,
            doc.ExternalNumber,
            doc.Comment,
            doc.PerformedBy,
            doc.Lines);

        return (renderer.RenderWriteOffAct(data), $"Акт_{doc.Number}.pdf");
    }
}
