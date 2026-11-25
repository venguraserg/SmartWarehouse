using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartWarehouse.API.Security;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Services;

namespace SmartWarehouse.API.Controllers;

/// <summary>
/// Инвентаризация по местам: начать (стеллаж), подсчёт по ячейкам, отметка «проверено»,
/// ведомость расхождений и проведение. Только кладовщик и администратор.
/// </summary>
[ApiController]
[Route("api/inventories")]
[Authorize(Roles = Roles.Warehouse)]
public class InventoriesController(IInventoryService inventories) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<InventorySummaryDto>> List(CancellationToken ct) => inventories.ListAsync(ct);

    /// <summary>Идущие инвентаризации, в которые входит место (страница места по QR).</summary>
    [HttpGet("open")]
    public Task<IReadOnlyList<InventorySummaryDto>> Open([FromQuery] int locationId, CancellationToken ct) =>
        inventories.FindOpenAsync(locationId, ct);

    [HttpPost]
    public Task<InventoryDto> Start(StartInventoryRequest request, CancellationToken ct) =>
        inventories.StartAsync(request, User.ToInventoryActor(), ct);

    [HttpGet("{id:int}")]
    public Task<InventoryDto> Get(int id, CancellationToken ct) => inventories.GetAsync(id, ct);

    [HttpGet("{id:int}/places/{locationId:int}")]
    public Task<InventoryPlaceDto> Place(int id, int locationId, CancellationToken ct) =>
        inventories.GetPlaceAsync(id, locationId, ct);

    /// <summary>Факт по позиции в месте (Counted = null — отменить подсчёт).</summary>
    [HttpPut("{id:int}/places/{locationId:int}/count")]
    public Task<InventoryPlaceDto> Count(int id, int locationId, InventoryCountRequest request, CancellationToken ct) =>
        inventories.CountAsync(id, locationId, request, User.ToInventoryActor(), ct);

    [HttpPut("{id:int}/places/{locationId:int}/check")]
    public Task<InventoryPlaceDto> Check(int id, int locationId, InventoryCheckRequest request, CancellationToken ct) =>
        inventories.CheckAsync(id, locationId, request.Checked, User.ToInventoryActor(), ct);

    [HttpGet("{id:int}/sheet")]
    public Task<InventorySheetDto> Sheet(int id, CancellationToken ct) => inventories.GetSheetAsync(id, ct);

    [HttpPost("{id:int}/post")]
    public Task<InventoryPostResultDto> Post(int id, CancellationToken ct) =>
        inventories.PostAsync(id, User.ToInventoryActor(), ct);

    [HttpPost("{id:int}/cancel")]
    public Task<InventorySummaryDto> Cancel(int id, CancellationToken ct) =>
        inventories.CancelAsync(id, User.ToInventoryActor(), ct);
}

internal static class InventoryAccess
{
    public static InventoryActor ToInventoryActor(this ClaimsPrincipal user) => new(
        user.EmployeeId(),
        user.FindFirstValue(AppClaims.DisplayName) ?? user.Identity?.Name ?? "—");
}
