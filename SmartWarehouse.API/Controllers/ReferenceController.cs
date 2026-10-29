using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartWarehouse.API.Security;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;

namespace SmartWarehouse.API.Controllers;

/// <summary>Справочник категорий номенклатуры.</summary>
[ApiController]
[Route("api/categories")]
public class CategoriesController(ICatalogService catalog) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<CategoryDto>> List(CancellationToken ct) => catalog.ListCategoriesAsync(ct);

    [HttpPost]
    [Authorize(Roles = Roles.Warehouse)]
    public async Task<ActionResult<CategoryDto>> Create(CreateCategoryRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await catalog.CreateCategoryAsync(request, ct));
}

/// <summary>Справочник единиц измерения.</summary>
[ApiController]
[Route("api/units")]
public class UnitsController(ICatalogService catalog) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<UnitDto>> List(CancellationToken ct) => catalog.ListUnitsAsync(ct);

    [HttpPost]
    [Authorize(Roles = Roles.Warehouse)]
    public async Task<ActionResult<UnitDto>> Create(CreateUnitRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await catalog.CreateUnitAsync(request, ct));
}

/// <summary>Справочник оборудования (дерево: участок → установка → узел).</summary>
[ApiController]
[Route("api/equipment")]
public class EquipmentController(ICatalogService catalog) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<EquipmentDto>> List(CancellationToken ct) => catalog.ListEquipmentAsync(ct);

    [HttpPost]
    [Authorize(Roles = Roles.Warehouse)]
    public async Task<ActionResult<EquipmentDto>> Create(CreateEquipmentRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await catalog.CreateEquipmentAsync(request, ct));
}
