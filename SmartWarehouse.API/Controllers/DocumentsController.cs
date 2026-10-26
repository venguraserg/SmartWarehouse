using Microsoft.AspNetCore.Mvc;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.API.Controllers;

/// <summary>Складские документы: приход, выдача, перемещение, списание, корректировка.</summary>
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

    /// <summary>Создать и провести документ. Проводится целиком или не проводится вовсе.</summary>
    [HttpPost]
    public async Task<ActionResult<DocumentDto>> Post(CreateDocumentRequest request, CancellationToken ct)
    {
        var document = await stock.PostDocumentAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = document.Id }, document);
    }
}
