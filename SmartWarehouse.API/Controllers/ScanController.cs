using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartWarehouse.API.Security;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Services;

namespace SmartWarehouse.API.Controllers;

/// <summary>Распознавание отсканированного кода и печать наклеек с QR-кодами.</summary>
[ApiController]
[Route("api")]
public class ScanController(IScanService scan, ILabelService labels) : ControllerBase
{
    /// <summary>
    /// Что за код: ссылка с нашей наклейки, код места хранения, артикул или заводской штрих-код позиции.
    /// Код — в параметре запроса (в нём могут быть «/» и другие символы).
    /// </summary>
    [HttpGet("scan")]
    public Task<ScanResultDto> Resolve([FromQuery] string code, CancellationToken ct) => scan.ResolveAsync(code, ct);

    /// <summary>Доступные раскладки листов наклеек.</summary>
    [HttpGet("labels/layouts")]
    public IEnumerable<LabelLayoutDto> Layouts() =>
        LabelLayout.All.Select(l => new LabelLayoutDto(l.Id, l.Title, l.Columns, l.Rows, l.WidthMm, l.HeightMm));

    /// <summary>PDF с наклейками на листах A4.</summary>
    [HttpPost("labels")]
    [Authorize(Roles = Roles.Warehouse)]
    public async Task<IActionResult> Labels(LabelRequest request, CancellationToken ct)
    {
        var (pdf, fileName) = await labels.RenderAsync(request, ct);
        return File(pdf, "application/pdf", fileName);
    }
}
