using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Application.Services;

/// <summary>
/// Справочник сотрудников. Удаления нет: сотрудник может быть в истории документов,
/// поэтому уволенного отмечают как неработающего — он пропадает из выбора, но остаётся в истории.
/// </summary>
public class EmployeeService(IEmployeeRepository repo) : IEmployeeService
{
    public async Task<IReadOnlyList<EmployeeDto>> ListAsync(string? search, bool includeInactive, CancellationToken ct = default)
    {
        var employees = await repo.ListAsync(string.IsNullOrWhiteSpace(search) ? null : search.Trim(), includeInactive, ct);
        return employees.Select(ToDto).ToList();
    }

    public async Task<EmployeeDto> GetAsync(int id, CancellationToken ct = default) =>
        ToDto(await FindAsync(id, ct));

    public async Task<EmployeeDto> CreateAsync(SaveEmployeeRequest request, CancellationToken ct = default)
    {
        var employee = new Employee();
        await ApplyAsync(employee, request, exceptId: null, ct);
        repo.Add(employee);
        await repo.SaveChangesAsync(ct);
        return ToDto(employee);
    }

    public async Task<EmployeeDto> UpdateAsync(int id, SaveEmployeeRequest request, CancellationToken ct = default)
    {
        var employee = await FindAsync(id, ct);
        await ApplyAsync(employee, request, exceptId: id, ct);
        await repo.SaveChangesAsync(ct);
        return ToDto(employee);
    }

    public async Task<IReadOnlyList<EmployeeMovementDto>> GetHistoryAsync(int id, int take = 200, CancellationToken ct = default)
    {
        await FindAsync(id, ct);
        var lines = await repo.GetHistoryAsync(id, Math.Clamp(take, 1, 1000), ct);
        return lines
            .Select(l =>
            {
                var issued = l.ToEmployeeId == id;
                return new EmployeeMovementDto(
                    l.DocumentId, l.Document.Number, l.Type, l.OccurredAt, l.ItemId, l.Item.Sku, l.Item.Name,
                    issued ? l.Quantity : -l.Quantity,
                    l.Item.Unit.Code,
                    issued ? l.FromLocation?.FullCode : l.ToLocation?.FullCode,
                    l.Document.EquipmentId);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<CustodyRowDto>> ListCustodyAsync(
        int? employeeId = null, int? itemId = null, string? search = null, CancellationToken ct = default)
    {
        var balances = await repo.ListCustodyAsync(employeeId, itemId, string.IsNullOrWhiteSpace(search) ? null : search.Trim(), ct);
        return balances
            .Select(b => new CustodyRowDto(
                b.EmployeeId, b.Employee.ShortName, b.Employee.Position, b.Employee.IsActive,
                b.ItemId, b.Item.Sku, b.Item.Name, b.Item.Unit.Code,
                b.Item.ImageUpdatedAt?.ToUnixTimeMilliseconds(), b.Quantity))
            .OrderBy(r => r.EmployeeName).ThenBy(r => r.ItemName)
            .ToList();
    }

    private async Task<Employee> FindAsync(int id, CancellationToken ct) =>
        await repo.GetAsync(id, ct) ?? throw new NotFoundException($"Сотрудник {id} не найден.");

    private async Task ApplyAsync(Employee employee, SaveEmployeeRequest request, int? exceptId, CancellationToken ct)
    {
        var lastName = Clean(request.LastName) ?? throw new DomainException("Укажите фамилию.");
        var firstName = Clean(request.FirstName) ?? throw new DomainException("Укажите имя.");
        var number = Clean(request.PersonnelNumber);

        if (number is not null && await repo.PersonnelNumberExistsAsync(number, exceptId, ct))
            throw new DomainException($"Табельный номер {number} уже есть у другого сотрудника.");

        employee.PersonnelNumber = number;
        employee.LastName = lastName;
        employee.FirstName = firstName;
        employee.MiddleName = Clean(request.MiddleName);
        employee.Position = Clean(request.Position);
        employee.Department = Clean(request.Department);
        employee.Phone = Clean(request.Phone);
        employee.IsActive = request.IsActive;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static EmployeeDto ToDto(Employee e) => new(
        e.Id, e.PersonnelNumber, e.LastName, e.FirstName, e.MiddleName, e.Position, e.Department, e.Phone,
        e.IsActive, e.FullName, e.ShortName);
}
