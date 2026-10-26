using Microsoft.AspNetCore.Mvc;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;

namespace SmartWarehouse.API.Controllers;

/// <summary>Складские операции и контроль минимальных остатков.</summary>
[ApiController]
[Route("api/stock")]
public class StockController(IStockService stock) : ControllerBase
{
    /// <summary>Приход, выдача, перемещение, списание или корректировка.</summary>
    [HttpPost("movements")]
    public async Task<ActionResult<MovementDto>> Register(MovementRequest request, CancellationToken ct)
    {
        var movement = await stock.RegisterAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, movement);
    }

    /// <summary>Позиции с остатком ниже минимума — основа для заявки на закупку.</summary>
    [HttpGet("below-minimum")]
    public Task<IReadOnlyList<LowStockItemDto>> BelowMinimum(CancellationToken ct) =>
        stock.GetBelowMinimumAsync(ct);
}
