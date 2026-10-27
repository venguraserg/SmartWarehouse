using Microsoft.AspNetCore.Mvc;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;

namespace SmartWarehouse.API.Controllers;

/// <summary>Контроль остатков.</summary>
[ApiController]
[Route("api/stock")]
public class StockController(IStockService stock) : ControllerBase
{
    /// <summary>
    /// Что лежит на складе по факту. Фильтры: warehouseId, location (код места — с вложенными),
    /// search (наименование или артикул), categoryId, belowMinimum.
    /// </summary>
    [HttpGet]
    public Task<StockSearchResult> Search([FromQuery] StockQuery query, CancellationToken ct) =>
        stock.SearchStockAsync(query, ct);

    /// <summary>Позиции с остатком ниже минимума и сколько нужно пополнить.</summary>
    [HttpGet("below-minimum")]
    public Task<IReadOnlyList<LowStockItemDto>> BelowMinimum(CancellationToken ct) =>
        stock.GetBelowMinimumAsync(ct);
}
