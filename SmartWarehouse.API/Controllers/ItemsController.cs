using Microsoft.AspNetCore.Mvc;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.API.Controllers;

/// <summary>Номенклатура, остатки и история движений по позиции.</summary>
[ApiController]
[Route("api/items")]
public class ItemsController(ICatalogService catalog, IStockService stock) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ItemDto>> List([FromQuery] string? search, [FromQuery] int? categoryId, CancellationToken ct) =>
        catalog.ListItemsAsync(search, categoryId, ct);

    /// <summary>Следующий свободный артикул вида «ЗЧ-000001».</summary>
    [HttpGet("next-sku")]
    public async Task<object> NextSku(CancellationToken ct) => new { sku = await catalog.SuggestSkuAsync(ct) };

    [HttpGet("{id:int}")]
    public Task<ItemDto> Get(int id, CancellationToken ct) => catalog.GetItemAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<ItemDto>> Create(CreateItemRequest request, CancellationToken ct)
    {
        var item = await catalog.CreateItemAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = item.Id }, item);
    }

    [HttpPut("{id:int}")]
    public Task<ItemDto> Update(int id, UpdateItemRequest request, CancellationToken ct) =>
        catalog.UpdateItemAsync(id, request, ct);

    /// <summary>Фото позиции.</summary>
    [HttpGet("{id:int}/image")]
    public async Task<IActionResult> GetImage(int id, CancellationToken ct)
    {
        var image = await catalog.GetImageAsync(id, ct);
        return File(image.Data, image.ContentType);
    }

    /// <summary>Загрузить или заменить фото (multipart/form-data, поле «file»; JPEG, PNG или WebP до 5 МБ).</summary>
    [HttpPut("{id:int}/image")]
    [RequestSizeLimit(ImageRules.MaxBytes + 64 * 1024)]
    public async Task<ItemDto> SetImage(int id, IFormFile file, CancellationToken ct)
    {
        if (file.Length > ImageRules.MaxBytes)
            throw new DomainException($"Изображение больше {ImageRules.MaxBytes / 1024 / 1024} МБ.");

        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        return await catalog.SetImageAsync(id, buffer.ToArray(), ct);
    }

    [HttpDelete("{id:int}/image")]
    public async Task<IActionResult> DeleteImage(int id, CancellationToken ct)
    {
        await catalog.DeleteImageAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:int}/balances")]
    public Task<IReadOnlyList<StockBalanceDto>> Balances(int id, CancellationToken ct) =>
        stock.GetBalancesAsync(id, ct);

    [HttpGet("{id:int}/movements")]
    public Task<IReadOnlyList<MovementDto>> Movements(int id, [FromQuery] int take = 100, CancellationToken ct = default) =>
        stock.GetMovementsAsync(id, take, ct);
}
