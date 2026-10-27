using Microsoft.AspNetCore.Mvc;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;

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
    public async Task<ActionResult<EmployeeDto>> Create(SaveEmployeeRequest request, CancellationToken ct)
    {
        var employee = await employees.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = employee.Id }, employee);
    }

    [HttpPut("{id:int}")]
    public Task<EmployeeDto> Update(int id, SaveEmployeeRequest request, CancellationToken ct) =>
        employees.UpdateAsync(id, request, ct);

    /// <summary>История подотчёта сотрудника: выдачи (+), списания и возвраты (−).</summary>
    [HttpGet("{id:int}/history")]
    public Task<IReadOnlyList<EmployeeMovementDto>> History(int id, [FromQuery] int take = 200, CancellationToken ct = default) =>
        employees.GetHistoryAsync(id, take, ct);

    /// <summary>Что числится за сотрудником.</summary>
    [HttpGet("{id:int}/custody")]
    public Task<IReadOnlyList<CustodyRowDto>> Custody(int id, CancellationToken ct) =>
        employees.ListCustodyAsync(employeeId: id, ct: ct);
}

/// <summary>Подотчёт: что числится за сотрудниками.</summary>
[ApiController]
[Route("api/custody")]
public class CustodyController(IEmployeeService employees) : ControllerBase
{
    /// <summary>Фильтры: employeeId, itemId, search (ФИО, табельный, артикул, наименование).</summary>
    [HttpGet]
    public Task<IReadOnlyList<CustodyRowDto>> List([FromQuery] int? employeeId, [FromQuery] int? itemId, [FromQuery] string? search, CancellationToken ct) =>
        employees.ListCustodyAsync(employeeId, itemId, search, ct);
}
