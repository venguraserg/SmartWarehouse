using Microsoft.AspNetCore.Mvc;
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
    public async Task<ActionResult<UnitDto>> Create(CreateUnitRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await catalog.CreateUnitAsync(request, ct));
}
