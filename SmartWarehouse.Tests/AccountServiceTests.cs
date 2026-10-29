using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Tests;

public class AccountServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeUserRepository _repo = new();
    private readonly AccountService _service;

    public AccountServiceTests()
    {
        _repo.Employees.Add(new Employee { Id = 1, LastName = "Петров", FirstName = "Сергей" });
        _repo.Employees.Add(new Employee { Id = 2, LastName = "Иванов", FirstName = "Иван", MiddleName = "Петрович" });
        _service = new AccountService(_repo, new FakeHasher(), new FixedTimeProvider(Now));
    }

    private Task<UserDto> CreateAsync(string login = "petrov", int? employee = 1, UserRole roles = UserRole.Employee, string password = "temp123") =>
        _service.CreateAsync(new CreateUserRequest(login, employee, roles, password));

    // ---------- Первый администратор ----------

    [Fact]
    public async Task Первый_запуск_создаёт_администратора_один_раз()
    {
        Assert.True(await _service.EnsureInitialAdminAsync("admin123"));
        Assert.False(await _service.EnsureInitialAdminAsync("admin123"));

        var admin = Assert.Single(_repo.Users);
        Assert.Equal("admin", admin.Login);
        Assert.True(admin.Has(UserRole.Admin));
        Assert.True(admin.MustChangePassword);
    }

    // ---------- Вход ----------

    [Fact]
    public async Task Вход_по_логину_без_учёта_регистра()
    {
        await CreateAsync();

        var user = await _service.LoginAsync(new LoginRequest(" PETROV ", "temp123"));

        Assert.Equal("petrov", user.Login);
        Assert.Equal("Петров С.", user.DisplayName);
        Assert.Equal(1, user.EmployeeId);
        Assert.True(user.MustChangePassword);
        Assert.Equal(Now, _repo.Users[0].LastLoginAt);
    }

    [Theory]
    [InlineData("petrov", "wrong")]
    [InlineData("nobody", "temp123")]
    [InlineData("", "")]
    public async Task Неверный_логин_или_пароль(string login, string password)
    {
        await CreateAsync();
        var ex = await Assert.ThrowsAsync<AuthenticationFailedException>(() => _service.LoginAsync(new LoginRequest(login, password)));
        Assert.Equal("Неверный логин или пароль.", ex.Message); // не подсказываем, что именно неверно
    }

    [Fact]
    public async Task Заблокированный_не_входит()
    {
        var user = await CreateAsync();
        await _service.UpdateAsync(user.Id, new UpdateUserRequest(1, UserRole.Employee, IsActive: false), actingUserId: 99);

        var ex = await Assert.ThrowsAsync<AuthenticationFailedException>(() => _service.LoginAsync(new LoginRequest("petrov", "temp123")));
        Assert.Contains("заблокирована", ex.Message);
    }

    // ---------- Пароли ----------

    [Fact]
    public async Task Смена_пароля_снимает_требование_смены()
    {
        var user = await CreateAsync();

        await _service.ChangePasswordAsync(user.Id, new ChangePasswordRequest("temp123", "MyNewPass1"));

        Assert.False(_repo.Users[0].MustChangePassword);
        await _service.LoginAsync(new LoginRequest("petrov", "MyNewPass1"));
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => _service.LoginAsync(new LoginRequest("petrov", "temp123")));
    }

    [Theory]
    [InlineData("wrong", "MyNewPass1")]   // неверный текущий
    [InlineData("temp123", "123")]        // короткий
    [InlineData("temp123", "temp123")]    // тот же
    [InlineData("temp123", "PETROV")]     // совпадает с логином
    public async Task Неправильная_смена_пароля(string current, string next)
    {
        var user = await CreateAsync();
        await Assert.ThrowsAsync<DomainException>(() => _service.ChangePasswordAsync(user.Id, new ChangePasswordRequest(current, next)));
    }

    [Fact]
    public async Task Сброс_пароля_администратором()
    {
        var user = await CreateAsync();
        await _service.ChangePasswordAsync(user.Id, new ChangePasswordRequest("temp123", "MyNewPass1"));

        await _service.ResetPasswordAsync(user.Id, new ResetPasswordRequest("reset456"));

        var logged = await _service.LoginAsync(new LoginRequest("petrov", "reset456"));
        Assert.True(logged.MustChangePassword);
    }

    // ---------- Создание и изменение ----------

    [Theory]
    [InlineData("иванов")]   // кириллица
    [InlineData("ab")]       // короче 3
    [InlineData("a b c")]    // пробелы
    public async Task Недопустимый_логин(string login)
    {
        await Assert.ThrowsAsync<DomainException>(() => CreateAsync(login));
    }

    [Fact]
    public async Task Логин_уникален_и_у_сотрудника_одна_учётная_запись()
    {
        await CreateAsync("petrov", 1);

        await Assert.ThrowsAsync<DomainException>(() => CreateAsync("PETROV", 2));
        await Assert.ThrowsAsync<DomainException>(() => CreateAsync("petrov2", 1));
    }

    [Fact]
    public async Task Роли_сотрудника_и_руководителя_требуют_сотрудника()
    {
        await Assert.ThrowsAsync<DomainException>(() => CreateAsync("boss", employee: null, roles: UserRole.Manager));
        await Assert.ThrowsAsync<DomainException>(() => CreateAsync("nobody", employee: 1, roles: UserRole.None));

        // Кладовщику или администратору сотрудник необязателен.
        var keeper = await CreateAsync("keeper", employee: null, roles: UserRole.Storekeeper);
        Assert.Equal(UserRole.Storekeeper, keeper.Roles);
    }

    [Fact]
    public async Task Нельзя_лишить_прав_себя_и_последнего_администратора()
    {
        await _service.EnsureInitialAdminAsync("admin123");
        var adminId = _repo.Users[0].Id;

        // Сам себя — нельзя.
        await Assert.ThrowsAsync<DomainException>(() =>
            _service.UpdateAsync(adminId, new UpdateUserRequest(null, UserRole.Storekeeper, true), actingUserId: adminId));

        // Другой администратор есть — снять можно.
        var second = await CreateAsync("chief", employee: 2, roles: UserRole.Admin | UserRole.Manager);
        await _service.UpdateAsync(adminId, new UpdateUserRequest(null, UserRole.Storekeeper, true), actingUserId: second.Id);

        // Последнего — нельзя.
        await Assert.ThrowsAsync<DomainException>(() =>
            _service.UpdateAsync(second.Id, new UpdateUserRequest(2, UserRole.Manager, true), actingUserId: adminId));
    }
}

/// <summary>«Хеш» для тестов: префикс + пароль (настоящий хешер проверяется отдельно).</summary>
internal class FakeHasher : IPasswordHasher
{
    public string Hash(string password) => "hash:" + password;
    public bool Verify(string hash, string password) => hash == "hash:" + password;
}

internal class FakeUserRepository : IUserRepository
{
    public List<UserAccount> Users { get; } = [];
    public List<Employee> Employees { get; } = [];
    private readonly List<UserAccount> _pending = [];

    public Task<bool> AnyAsync(CancellationToken ct = default) => Task.FromResult(Users.Count > 0);

    public Task<UserAccount?> GetByLoginAsync(string login, CancellationToken ct = default) =>
        Task.FromResult(Users.FirstOrDefault(u => u.Login == login));

    public Task<UserAccount?> GetAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Users.FirstOrDefault(u => u.Id == id));

    public Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<UserAccount>>(Users.ToList());

    public Task<bool> LoginExistsAsync(string login, CancellationToken ct = default) =>
        Task.FromResult(Users.Any(u => u.Login == login));

    public Task<bool> EmployeeHasAccountAsync(int employeeId, int? exceptUserId, CancellationToken ct = default) =>
        Task.FromResult(Users.Any(u => u.EmployeeId == employeeId && u.Id != exceptUserId));

    public Task<int> CountActiveAdminsAsync(int? exceptUserId, CancellationToken ct = default) =>
        Task.FromResult(Users.Count(u => u.IsActive && u.Has(UserRole.Admin) && u.Id != exceptUserId));

    public Task<Employee?> GetEmployeeAsync(int employeeId, CancellationToken ct = default) =>
        Task.FromResult(Employees.FirstOrDefault(e => e.Id == employeeId));

    public void Add(UserAccount user) => _pending.Add(user);

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var u in _pending)
        {
            u.Id = Users.Count + 1;
            Users.Add(u);
        }
        _pending.Clear();
        return Task.CompletedTask;
    }
}

public class IdentityPasswordHasherTests
{
    [Fact]
    public void Настоящий_хеш_проверяется_и_не_хранит_пароль()
    {
        var hasher = new SmartWarehouse.Infrastructure.Security.IdentityPasswordHasher();
        var hash = hasher.Hash("Секрет123");

        Assert.DoesNotContain("Секрет123", hash);
        Assert.True(hasher.Verify(hash, "Секрет123"));
        Assert.False(hasher.Verify(hash, "секрет123"));
        Assert.NotEqual(hash, hasher.Hash("Секрет123")); // соль — каждый раз разный хеш
    }
}
