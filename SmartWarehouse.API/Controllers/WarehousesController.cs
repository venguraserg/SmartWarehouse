using Microsoft.AspNetCore.Mvc;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;

namespace SmartWarehouse.API.Controllers;

/// <summary>Склады и дерево мест хранения.</summary>
[ApiController]
[Route("api/warehouses")]
public class WarehousesController(ILocationService locations) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<WarehouseDto>> List(CancellationToken ct) => locations.ListWarehousesAsync(ct);

    [HttpPost]
    public async Task<ActionResult<WarehouseDto>> Create(CreateWarehouseRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await locations.CreateWarehouseAsync(request, ct));

    /// <summary>Дерево мест хранения склада.</summary>
    [HttpGet("{id:int}/locations")]
    public Task<IReadOnlyList<LocationNodeDto>> Tree(int id, CancellationToken ct) =>
        locations.GetLocationTreeAsync(id, ct);

    /// <summary>Добавить место хранения (parentId = null — верхний уровень склада).</summary>
    [HttpPost("{id:int}/locations")]
    public async Task<ActionResult<LocationDto>> AddLocation(int id, CreateLocationRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await locations.AddLocationAsync(id, request, ct));
}

/// <summary>Места хранения: поиск по коду (QR) и содержимое.</summary>
[ApiController]
[Route("api/locations")]
public class LocationsController(ILocationService locations) : ControllerBase
{
    /// <summary>Найти место по полному коду, например СК1-A-01-03.</summary>
    [HttpGet("by-code/{fullCode}")]
    public Task<LocationDto> ByCode(string fullCode, CancellationToken ct) => locations.GetByCodeAsync(fullCode, ct);

    /// <summary>Что лежит в месте хранения.</summary>
    [HttpGet("{id:int}/content")]
    public Task<IReadOnlyList<LocationContentDto>> Content(int id, CancellationToken ct) =>
        locations.GetContentAsync(id, ct);
}
