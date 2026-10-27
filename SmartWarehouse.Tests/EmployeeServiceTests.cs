using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Tests;

public class EmployeeServiceTests
{
    private readonly FakeEmployeeRepository _repo = new();
    private readonly EmployeeService _service;

    public EmployeeServiceTests() => _service = new EmployeeService(_repo);

    private static SaveEmployeeRequest Request(string last = "Иванов", string first = "Иван", string? middle = "Петрович", string? number = null, bool active = true) =>
        new(number, last, first, middle, "Слесарь-ремонтник", "Участок розлива", "+375 29 000-00-00", active);

    [Fact]
    public async Task Создание_сотрудника_с_полным_и_кратким_именем()
    {
        var e = await _service.CreateAsync(Request(last: "  Иванов ", number: "0123"));

        Assert.Equal("Иванов", e.LastName);
        Assert.Equal("Иванов Иван Петрович", e.FullName);
        Assert.Equal("Иванов И. П.", e.ShortName);
        Assert.Equal("0123", e.PersonnelNumber);
        Assert.True(e.IsActive);
    }

    [Fact]
    public async Task Без_отчества_краткое_имя_без_второго_инициала()
    {
        var e = await _service.CreateAsync(Request(middle: null));
        Assert.Equal("Иванов И.", e.ShortName);
        Assert.Equal("Иванов Иван", e.FullName);
    }

    [Theory]
    [InlineData("", "Иван")]
    [InlineData("Иванов", " ")]
    public async Task Фамилия_и_имя_обязательны(string last, string first)
    {
        await Assert.ThrowsAsync<DomainException>(() => _service.CreateAsync(Request(last, first)));
        Assert.Empty(_repo.Employees);
    }

    [Fact]
    public async Task Табельный_номер_уникален_но_необязателен()
    {
        await _service.CreateAsync(Request(number: "100"));
        await Assert.ThrowsAsync<DomainException>(() => _service.CreateAsync(Request(last: "Петров", number: "100")));

        // Без номера — сколько угодно.
        await _service.CreateAsync(Request(last: "Петров"));
        await _service.CreateAsync(Request(last: "Сидоров"));
        Assert.Equal(3, _repo.Employees.Count);
    }

    [Fact]
    public async Task Изменение_и_увольнение()
    {
        var e = await _service.CreateAsync(Request(number: "100"));

        // Свой табельный номер при сохранении не считается занятым.
        var updated = await _service.UpdateAsync(e.Id, Request(last: "Иванова", number: "100", active: false));

        Assert.Equal("Иванова", updated.LastName);
        Assert.False(updated.IsActive);
        Assert.Empty(await _service.ListAsync(null, includeInactive: false));
        Assert.Single(await _service.ListAsync(null, includeInactive: true));
    }

    [Fact]
    public async Task Несуществующий_сотрудник_NotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetAsync(5));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.UpdateAsync(5, Request()));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetIssuesAsync(5));
    }
}

/// <summary>Справочник сотрудников в памяти.</summary>
internal class FakeEmployeeRepository : IEmployeeRepository
{
    public List<Employee> Employees { get; } = [];
    private readonly List<Employee> _pending = [];

    public Task<IReadOnlyList<Employee>> ListAsync(string? search, bool includeInactive, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Employee>>(Employees
            .Where(e => includeInactive || e.IsActive)
            .Where(e => search is null || e.FullName.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList());

    public Task<Employee?> GetAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Employees.FirstOrDefault(e => e.Id == id));

    public Task<bool> PersonnelNumberExistsAsync(string number, int? exceptId, CancellationToken ct = default) =>
        Task.FromResult(Employees.Any(e => e.PersonnelNumber == number && e.Id != exceptId));

    public void Add(Employee employee) => _pending.Add(employee);

    public Task<IReadOnlyList<StockMovement>> GetIssuesAsync(int employeeId, int take, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<StockMovement>>([]);

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var e in _pending)
        {
            e.Id = Employees.Count + 1;
            Employees.Add(e);
        }
        _pending.Clear();
        return Task.CompletedTask;
    }
}
