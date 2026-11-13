using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartWarehouse.API.Security;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.API.Controllers;

/// <summary>
/// Приёмка учётных материалов: накладная на внутреннее перемещение из 1С (Excel) → место «Приёмка» →
/// размещение по ячейкам; расхождения факта с накладной — отметкой для бухгалтерии.
/// </summary>
[ApiController]
[Route("api/receiving")]
[Authorize(Roles = Roles.Warehouse)]
public class ReceivingController(IImportFileReader reader, IReceivingService receiving) : ControllerBase
{
    private const long MaxFileBytes = 20 * 1024 * 1024;

    /// <summary>Накладная (multipart, поле «file», .xls или .xlsx). apply=false — проверка, apply=true — приём.</summary>
    [HttpPost("invoice")]
    [RequestSizeLimit(MaxFileBytes + 1024 * 1024)]
    public async Task<InvoiceImportResult> Invoice(IFormFile file, [FromQuery] int warehouseId, [FromQuery] bool apply, CancellationToken ct)
    {
        if (file.Length == 0)
            throw new DomainException("Файл пустой.");
        if (file.Length > MaxFileBytes)
            throw new DomainException("Файл больше 20 МБ.");
        await using var stream = file.OpenReadStream();
        var invoice = reader.ReadInvoice(stream, file.FileName);
        return await receiving.ImportInvoiceAsync(invoice, warehouseId, apply,
            new ImportActor(User.EmployeeId(), User.FindFirst(AppClaims.DisplayName)?.Value), ct);
    }

    /// <summary>Что лежит в «Приёмке» и ждёт размещения.</summary>
    [HttpGet("pending")]
    public Task<IReadOnlyList<PendingPlacementDto>> Pending(CancellationToken ct) => receiving.ListPendingAsync(ct);

    [HttpGet("discrepancies")]
    public Task<IReadOnlyList<DiscrepancyDto>> Discrepancies([FromQuery] bool all, CancellationToken ct) =>
        receiving.ListDiscrepanciesAsync(all, ct);

    [HttpPost("discrepancies")]
    public Task<DiscrepancyDto> Report(ReportDiscrepancyRequest request, CancellationToken ct) =>
        receiving.ReportDiscrepancyAsync(request, User.FindFirst(AppClaims.DisplayName)?.Value, ct);

    [HttpPost("discrepancies/{id:int}/resolve")]
    public Task<DiscrepancyDto> Resolve(int id, ResolveDiscrepancyRequest request, CancellationToken ct) =>
        receiving.ResolveDiscrepancyAsync(id, request.Resolution, ct);
}
