using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using SmartWarehouse.API.Security;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.API.Controllers;

/// <summary>Справочник сотрудников. Удаления нет — уволенных отмечают isActive = false.</summary>
[ApiController]
[Route("api/employees")]
public class EmployeesController(IEmployeeService employees) : ControllerBase
{
    /// <summary>Список; search — ФИО, табельный номер, должность, подразделение.</summary>
    [HttpGet]
    public Task<IReadOnlyList<EmployeeDto>> List([FromQuery] string? search, [FromQuery] bool includeInactive = false, CancellationToken ct = default) =>
        employees.ListAsync(search, includeInactive, ct);

    [HttpGet("{id:int}")]
    public Task<EmployeeDto> Get(int id, CancellationToken ct) => employees.GetAsync(id, ct);

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<EmployeeDto>> Create(SaveEmployeeRequest request, CancellationToken ct)
    {
        var employee = await employees.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = employee.Id }, employee);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public Task<EmployeeDto> Update(int id, SaveEmployeeRequest request, CancellationToken ct) =>
        employees.UpdateAsync(id, request, ct);

    /// <summary>История подотчёта сотрудника: выдачи (+), списания и возвраты (−).</summary>
    [HttpGet("{id:int}/history")]
    public async Task<IReadOnlyList<EmployeeMovementDto>> History(int id, [FromQuery] int take = 200, CancellationToken ct = default)
    {
        await EnsureVisibleAsync(id, ct);
        return await employees.GetHistoryAsync(id, take, ct);
    }

    /// <summary>Что числится за сотрудником.</summary>
    [HttpGet("{id:int}/custody")]
    public async Task<IReadOnlyList<CustodyRowDto>> Custody(int id, CancellationToken ct)
    {
        await EnsureVisibleAsync(id, ct);
        return await employees.ListCustodyAsync(employeeId: id, ct: ct);
    }

    /// <summary>Сотрудник видит только свой подотчёт, руководитель — свой и подчинённых.</summary>
    private async Task EnsureVisibleAsync(int employeeId, CancellationToken ct)
    {
        var visible = await CustodyVisibility.VisibleEmployeesAsync(User, employees, ct);
        if (visible is not null && !visible.Contains(employeeId))
            throw new ForbiddenException("Нет доступа к подотчёту этого сотрудника.");
    }
}

/// <summary>Кто чей подотчёт видит.</summary>
internal static class CustodyVisibility
{
    /// <returns>null — все сотрудники (кладовщик, администратор); иначе — разрешённые.</returns>
    public static async Task<IReadOnlyCollection<int>?> VisibleEmployeesAsync(ClaimsPrincipal user, IEmployeeService employees, CancellationToken ct)
    {
        if (user.SeesAllCustody())
            return null;
        if (user.EmployeeId() is not int me)
            return Array.Empty<int>();
        return user.IsInRole(Roles.Manager) ? await employees.GetTeamAsync(me, ct) : [me];
    }
}

/// <summary>Подотчёт: что числится за сотрудниками.</summary>
[ApiController]
[Route("api/custody")]
public class CustodyController(IEmployeeService employees) : ControllerBase
{
    /// <summary>Фильтры: employeeId, itemId, search (ФИО, табельный, артикул, наименование).</summary>
    [HttpGet]
    public async Task<IReadOnlyList<CustodyRowDto>> List([FromQuery] int? employeeId, [FromQuery] int? itemId, [FromQuery] string? search, CancellationToken ct)
    {
        var visible = await CustodyVisibility.VisibleEmployeesAsync(User, employees, ct);
        return await employees.ListCustodyAsync(employeeId, itemId, search, visible, ct);
    }
}
