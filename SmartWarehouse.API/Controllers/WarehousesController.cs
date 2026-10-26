using Microsoft.AspNetCore.Mvc;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;

namespace SmartWarehouse.API.Controllers;

/// <summary>Склады и места хранения (ячейки).</summary>
[ApiController]
[Route("api/warehouses")]
public class WarehousesController(ICatalogService catalog) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<WarehouseDto>> List(CancellationToken ct) => catalog.ListWarehousesAsync(ct);

    [HttpPost]
    public async Task<ActionResult<WarehouseDto>> Create(CreateWarehouseRequest request, CancellationToken ct)
    {
        var warehouse = await catalog.CreateWarehouseAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, warehouse);
    }

    [HttpPost("{id:int}/locations")]
    public async Task<ActionResult<LocationDto>> AddLocation(int id, CreateLocationRequest request, CancellationToken ct)
    {
        var location = await catalog.AddLocationAsync(id, request, ct);
        return StatusCode(StatusCodes.Status201Created, location);
    }
}
