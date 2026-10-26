using Microsoft.AspNetCore.Mvc;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;

namespace SmartWarehouse.API.Controllers;

/// <summary>Номенклатура, остатки и история движений по позиции.</summary>
[ApiController]
[Route("api/items")]
public class ItemsController(ICatalogService catalog, IStockService stock) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ItemDto>> List([FromQuery] string? search, CancellationToken ct) =>
        catalog.ListItemsAsync(search, ct);

    [HttpGet("{id:int}")]
    public Task<ItemDto> Get(int id, CancellationToken ct) => catalog.GetItemAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<ItemDto>> Create(CreateItemRequest request, CancellationToken ct)
    {
        var item = await catalog.CreateItemAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = item.Id }, item);
    }

    [HttpGet("{id:int}/balances")]
    public Task<IReadOnlyList<StockBalanceDto>> Balances(int id, CancellationToken ct) =>
        stock.GetBalancesAsync(id, ct);

    [HttpGet("{id:int}/movements")]
    public Task<IReadOnlyList<MovementDto>> Movements(int id, [FromQuery] int take = 100, CancellationToken ct = default) =>
        stock.GetMovementsAsync(id, take, ct);
}
