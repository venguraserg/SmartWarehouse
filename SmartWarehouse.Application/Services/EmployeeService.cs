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
        int? employeeId = null, int? itemId = null, string? search = null,
        IReadOnlyCollection<int>? onlyEmployeeIds = null, CancellationToken ct = default)
    {
        var balances = await repo.ListCustodyAsync(
            employeeId, itemId, string.IsNullOrWhiteSpace(search) ? null : search.Trim(), onlyEmployeeIds, ct);

        // Давность подотчёта и резерв в актах на утверждении — по тем же сотрудникам.
        var employeeIds = balances.Select(b => b.EmployeeId).Distinct().ToList();
        IReadOnlyList<IssueRecord> issues = employeeIds.Count == 0 ? [] : await repo.ListIssuesAsync(employeeIds, ct);
        IReadOnlyList<ReservedRecord> reserved = employeeIds.Count == 0 ? [] : await repo.ListReservedAsync(employeeIds, ct);
        var issuesByKey = issues.ToLookup(i => (i.EmployeeId, i.ItemId));
        var reservedByKey = reserved
            .GroupBy(r => (r.EmployeeId, r.ItemId))
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));

        return balances
            .Select(b => new CustodyRowDto(
                b.EmployeeId, b.Employee.ShortName, b.Employee.Position, b.Employee.IsActive,
                b.ItemId, b.Item.Sku, b.Item.Name, b.Item.Unit.Code,
                b.Item.ImageUpdatedAt?.ToUnixTimeMilliseconds(), b.Quantity,
                HeldSince(b.Quantity, issuesByKey[(b.EmployeeId, b.ItemId)]),
                reservedByKey.GetValueOrDefault((b.EmployeeId, b.ItemId))))
            .OrderBy(r => r.EmployeeName).ThenBy(r => r.ItemName)
            .ToList();
    }

    public async Task<IReadOnlyCollection<int>> GetTeamAsync(int employeeId, CancellationToken ct = default) =>
        Team(await repo.ListHierarchyAsync(ct), employeeId);

    /// <summary>Сам сотрудник и все его подчинённые по цепочке «Руководитель».</summary>
    internal static HashSet<int> Team(IReadOnlyList<(int Id, int? ManagerId)> hierarchy, int employeeId)
    {
        var byManager = hierarchy.Where(h => h.ManagerId is not null).ToLookup(h => h.ManagerId!.Value, h => h.Id);

        var team = new HashSet<int> { employeeId };
        var queue = new Queue<int>([employeeId]);
        while (queue.Count > 0)
        {
            foreach (var subordinate in byManager[queue.Dequeue()])
            {
                if (team.Add(subordinate))
                    queue.Enqueue(subordinate);
            }
        }
        return team;
    }

    /// <summary>
    /// С какой даты числится остаток: выдачи списываются по принципу «первой выдана — первой израсходована»,
    /// поэтому остаток — это последние выдачи; дата самой ранней из них и есть ответ.
    /// </summary>
    internal static DateTimeOffset? HeldSince(decimal quantity, IEnumerable<IssueRecord> issues)
    {
        DateTimeOffset? since = null;
        var remaining = quantity;
        foreach (var issue in issues.OrderByDescending(i => i.OccurredAt))
        {
            since = issue.OccurredAt;
            remaining -= issue.Quantity;
            if (remaining <= 0)
                break;
        }
        return since;
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

        var manager = await ValidateManagerAsync(request.ManagerId, exceptId, ct);

        employee.PersonnelNumber = number;
        employee.LastName = lastName;
        employee.FirstName = firstName;
        employee.MiddleName = Clean(request.MiddleName);
        employee.Position = Clean(request.Position);
        employee.Department = Clean(request.Department);
        employee.Phone = Clean(request.Phone);
        employee.IsActive = request.IsActive;
        employee.ManagerId = manager?.Id;
        employee.Manager = manager;
    }

    /// <summary>Руководитель должен существовать и не может быть самим сотрудником или его подчинённым (цикл).</summary>
    private async Task<Employee?> ValidateManagerAsync(int? managerId, int? employeeId, CancellationToken ct)
    {
        if (managerId is not int id)
            return null;
        if (id == employeeId)
            throw new DomainException("Сотрудник не может быть руководителем самому себе.");

        var manager = await repo.GetAsync(id, ct)
            ?? throw new NotFoundException($"Руководитель {id} не найден.");

        if (employeeId is int self)
        {
            // Поднимаемся по цепочке руководителей нового руководителя: если встретим сотрудника — получится цикл.
            var hierarchy = (await repo.ListHierarchyAsync(ct)).ToDictionary(h => h.Id, h => h.ManagerId);
            var current = (int?)id;
            var guard = 0;
            while (current is int c && guard++ < 1000)
            {
                if (c == self)
                    throw new DomainException($"{manager.ShortName} подчиняется этому сотруднику — получится замкнутая цепочка руководителей.");
                current = hierarchy.GetValueOrDefault(c);
            }
        }

        return manager;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static EmployeeDto ToDto(Employee e) => new(
        e.Id, e.PersonnelNumber, e.LastName, e.FirstName, e.MiddleName, e.Position, e.Department, e.Phone,
        e.IsActive, e.FullName, e.ShortName, e.ManagerId, e.Manager?.ShortName);
}
