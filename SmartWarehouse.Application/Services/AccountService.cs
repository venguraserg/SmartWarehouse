using System.Text.RegularExpressions;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Application.Services;

/// <summary>Учётные записи: вход, смена и сброс пароля, управление пользователями.</summary>
public partial class AccountService(IUserRepository repo, IPasswordHasher hasher, TimeProvider clock)
    : IAuthService, IUserService
{
    public const int MinPasswordLength = 6;
    public const string InitialAdminLogin = "admin";

    private const UserRole AllRoles = UserRole.Employee | UserRole.Manager | UserRole.Storekeeper | UserRole.Admin;

    [GeneratedRegex("^[a-z0-9._-]{3,32}$")]
    private static partial Regex LoginPattern();

    // ---------- Вход ----------

    public async Task<CurrentUserDto> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        const string wrong = "Неверный логин или пароль.";

        var login = NormalizeLogin(request.Login);
        if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(request.Password))
            throw new AuthenticationFailedException(wrong);

        var user = await repo.GetByLoginAsync(login, ct);
        if (user is null || !hasher.Verify(user.PasswordHash, request.Password))
            throw new AuthenticationFailedException(wrong);

        if (!user.IsActive)
            throw new AuthenticationFailedException("Учётная запись заблокирована. Обратитесь к администратору.");

        user.LastLoginAt = clock.GetUtcNow();
        await repo.SaveChangesAsync(ct);
        return ToCurrent(user);
    }

    public async Task<CurrentUserDto> GetCurrentAsync(int userId, CancellationToken ct = default)
    {
        var user = await repo.GetAsync(userId, ct);
        if (user is null || !user.IsActive)
            throw new AuthenticationFailedException("Учётная запись не найдена или заблокирована.");
        return ToCurrent(user);
    }

    public async Task ChangePasswordAsync(int userId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        var user = await repo.GetAsync(userId, ct)
            ?? throw new NotFoundException("Учётная запись не найдена.");

        if (!hasher.Verify(user.PasswordHash, request.CurrentPassword ?? ""))
            throw new DomainException("Текущий пароль указан неверно.");

        ValidatePassword(request.NewPassword, user.Login);
        if (request.NewPassword == request.CurrentPassword)
            throw new DomainException("Новый пароль должен отличаться от текущего.");

        user.PasswordHash = hasher.Hash(request.NewPassword);
        user.MustChangePassword = false;
        await repo.SaveChangesAsync(ct);
    }

    public async Task<bool> EnsureInitialAdminAsync(string password, CancellationToken ct = default)
    {
        if (await repo.AnyAsync(ct))
            return false;

        repo.Add(new UserAccount
        {
            Login = InitialAdminLogin,
            PasswordHash = hasher.Hash(password),
            Roles = UserRole.Admin | UserRole.Storekeeper,
            MustChangePassword = true,
            CreatedAt = clock.GetUtcNow()
        });
        await repo.SaveChangesAsync(ct);
        return true;
    }

    // ---------- Управление пользователями ----------

    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct = default) =>
        (await repo.ListAsync(ct)).Select(ToDto).ToList();

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var login = NormalizeLogin(request.Login);
        if (!LoginPattern().IsMatch(login))
            throw new DomainException("Логин: 3–32 символа, латинские буквы, цифры, точка, дефис или подчёркивание (например, ivanov).");
        if (await repo.LoginExistsAsync(login, ct))
            throw new DomainException($"Логин «{login}» уже занят.");

        var employee = await ValidateRolesAndEmployeeAsync(request.Roles, request.EmployeeId, exceptUserId: null, ct);
        ValidatePassword(request.TemporaryPassword, login);

        var user = new UserAccount
        {
            Login = login,
            PasswordHash = hasher.Hash(request.TemporaryPassword),
            EmployeeId = employee?.Id,
            Employee = employee,
            Roles = request.Roles,
            MustChangePassword = true,
            CreatedAt = clock.GetUtcNow()
        };
        repo.Add(user);
        await repo.SaveChangesAsync(ct);
        return ToDto(user);
    }

    public async Task<UserDto> UpdateAsync(int id, UpdateUserRequest request, int actingUserId, CancellationToken ct = default)
    {
        var user = await repo.GetAsync(id, ct)
            ?? throw new NotFoundException($"Учётная запись {id} не найдена.");

        var employee = await ValidateRolesAndEmployeeAsync(request.Roles, request.EmployeeId, exceptUserId: id, ct);

        var staysAdmin = request.IsActive && (request.Roles & UserRole.Admin) != 0;
        if (user.Has(UserRole.Admin) && user.IsActive && !staysAdmin)
        {
            if (id == actingUserId)
                throw new DomainException("Нельзя снять с себя права администратора или заблокировать себя.");
            if (await repo.CountActiveAdminsAsync(exceptUserId: id, ct) == 0)
                throw new DomainException("Должен остаться хотя бы один активный администратор.");
        }

        user.EmployeeId = employee?.Id;
        user.Employee = employee;
        user.Roles = request.Roles;
        user.IsActive = request.IsActive;
        await repo.SaveChangesAsync(ct);
        return ToDto(user);
    }

    public async Task ResetPasswordAsync(int id, ResetPasswordRequest request, CancellationToken ct = default)
    {
        var user = await repo.GetAsync(id, ct)
            ?? throw new NotFoundException($"Учётная запись {id} не найдена.");

        ValidatePassword(request.TemporaryPassword, user.Login);
        user.PasswordHash = hasher.Hash(request.TemporaryPassword);
        user.MustChangePassword = true;
        await repo.SaveChangesAsync(ct);
    }

    // ---------- Проверки ----------

    private async Task<Employee?> ValidateRolesAndEmployeeAsync(UserRole roles, int? employeeId, int? exceptUserId, CancellationToken ct)
    {
        if (roles == UserRole.None)
            throw new DomainException("Выберите хотя бы одну роль.");
        if ((roles & ~AllRoles) != 0)
            throw new DomainException("Неизвестная роль.");

        if (employeeId is not int empId)
        {
            if ((roles & (UserRole.Employee | UserRole.Manager)) != 0)
                throw new DomainException("Для ролей «Сотрудник» и «Руководитель» укажите сотрудника: подотчёт и акты ведутся по нему.");
            return null;
        }

        var employee = await repo.GetEmployeeAsync(empId, ct)
            ?? throw new NotFoundException($"Сотрудник {empId} не найден.");
        if (await repo.EmployeeHasAccountAsync(empId, exceptUserId, ct))
            throw new DomainException($"У сотрудника {employee.ShortName} уже есть учётная запись.");
        return employee;
    }

    private static void ValidatePassword(string? password, string login)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            throw new DomainException($"Пароль должен быть не короче {MinPasswordLength} символов.");
        if (string.Equals(password, login, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Пароль не должен совпадать с логином.");
    }

    private static string NormalizeLogin(string? login) => login?.Trim().ToLowerInvariant() ?? "";

    private static string DisplayName(UserAccount u) => u.Employee?.ShortName ?? u.Login;

    private static CurrentUserDto ToCurrent(UserAccount u) =>
        new(u.Id, u.Login, DisplayName(u), u.EmployeeId, u.Roles, u.MustChangePassword);

    private static UserDto ToDto(UserAccount u) =>
        new(u.Id, u.Login, u.EmployeeId, u.Employee?.ShortName, u.Roles, u.IsActive, u.MustChangePassword, u.LastLoginAt);
}
