using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartWarehouse.API.Security;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Services;

namespace SmartWarehouse.API.Controllers;

/// <summary>
/// Исправление справочников — только администратор: склады, места хранения (код, тип, вложение),
/// категории, единицы, оборудование. Удаляется только то, что не использовалось в учёте.
/// </summary>
[ApiController]
[Route("api")]
[Authorize(Roles = Roles.Admin)]
public class ReferenceAdminController(IReferenceAdminService admin) : ControllerBase
{
    [HttpPut("warehouses/{id:int}")]
    public Task<ReferenceChangeResult> UpdateWarehouse(int id, UpdateWarehouseRequest request, CancellationToken ct) =>
        admin.UpdateWarehouseAsync(id, request, ct);

    [HttpDelete("warehouses/{id:int}")]
    public Task<ReferenceChangeResult> DeleteWarehouse(int id, CancellationToken ct) => admin.DeleteWarehouseAsync(id, ct);

    [HttpPut("locations/{id:int}")]
    public Task<ReferenceChangeResult> UpdateLocation(int id, UpdateLocationRequest request, CancellationToken ct) =>
        admin.UpdateLocationAsync(id, request, ct);

    [HttpDelete("locations/{id:int}")]
    public Task<ReferenceChangeResult> DeleteLocation(int id, CancellationToken ct) => admin.DeleteLocationAsync(id, ct);

    [HttpPut("categories/{id:int}")]
    public Task<ReferenceChangeResult> UpdateCategory(int id, UpdateCategoryRequest request, CancellationToken ct) =>
        admin.UpdateCategoryAsync(id, request, ct);

    [HttpDelete("categories/{id:int}")]
    public Task<ReferenceChangeResult> DeleteCategory(int id, CancellationToken ct) => admin.DeleteCategoryAsync(id, ct);

    [HttpPut("units/{id:int}")]
    public Task<ReferenceChangeResult> UpdateUnit(int id, UpdateUnitRequest request, CancellationToken ct) =>
        admin.UpdateUnitAsync(id, request, ct);

    [HttpDelete("units/{id:int}")]
    public Task<ReferenceChangeResult> DeleteUnit(int id, CancellationToken ct) => admin.DeleteUnitAsync(id, ct);

    [HttpPut("equipment/{id:int}")]
    public Task<ReferenceChangeResult> UpdateEquipment(int id, UpdateEquipmentRequest request, CancellationToken ct) =>
        admin.UpdateEquipmentAsync(id, request, ct);

    [HttpDelete("equipment/{id:int}")]
    public Task<ReferenceChangeResult> DeleteEquipment(int id, CancellationToken ct) => admin.DeleteEquipmentAsync(id, ct);
}
