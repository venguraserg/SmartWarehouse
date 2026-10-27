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

    /// <summary>Что выдавалось сотруднику.</summary>
    [HttpGet("{id:int}/issues")]
    public Task<IReadOnlyList<EmployeeIssueDto>> Issues(int id, [FromQuery] int take = 200, CancellationToken ct = default) =>
        employees.GetIssuesAsync(id, take, ct);
}
