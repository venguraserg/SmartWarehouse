using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartWarehouse.API.Security;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.API.Controllers;

/// <summary>Складские документы: приход, выдача, возврат, перемещение, корректировка; списание — по актам.</summary>
[ApiController]
[Route("api/documents")]
public class DocumentsController(IStockService stock) : ControllerBase
{
    /// <summary>Список документов, новые первыми; можно отфильтровать по типу.</summary>
    [HttpGet]
    public Task<IReadOnlyList<DocumentSummaryDto>> List([FromQuery] MovementType? type, [FromQuery] int take = 100, CancellationToken ct = default) =>
        stock.ListDocumentsAsync(type, take, ct);

    [HttpGet("{id:long}")]
    public Task<DocumentDto> Get(long id, CancellationToken ct) => stock.GetDocumentAsync(id, ct);

    /// <summary>Акт на списание (PDF) — только для документов списания.</summary>
    [HttpGet("{id:long}/act")]
    public async Task<IActionResult> WriteOffAct(long id, [FromServices] IActService acts, CancellationToken ct)
    {
        var (pdf, fileName) = await acts.GetWriteOffActAsync(id, User.ToActActor(), ct);
        return File(pdf, "application/pdf", fileName);
    }

    /// <summary>
    /// Создать и провести документ. Проводится целиком или не проводится вовсе.
    /// Списание так не проводится — только утверждением акта на списание (api/acts).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = Roles.Warehouse)]
    public async Task<ActionResult<DocumentDto>> Post(CreateDocumentRequest request, CancellationToken ct)
    {
        // «Кто проводит» по умолчанию — сотрудник, под чьей учётной записью вошли.
        if (request.PerformedById is null && string.IsNullOrWhiteSpace(request.PerformedBy) && User.EmployeeId() is int me)
            request = request with { PerformedById = me };

        var document = await stock.PostDocumentAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = document.Id }, document);
    }
}
