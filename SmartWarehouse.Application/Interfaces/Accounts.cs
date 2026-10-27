using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Domain.Entities;

namespace SmartWarehouse.Application.Interfaces;

/// <summary>Хеширование паролей (реализация — в Infrastructure).</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string hash, string password);
}

/// <summary>Доступ к учётным записям.</summary>
public interface IUserRepository
{
    Task<bool> AnyAsync(CancellationToken ct = default);

    /// <summary>Учётная запись по логину (отслеживаемая, с сотрудником).</summary>
    Task<UserAccount?> GetByLoginAsync(string login, CancellationToken ct = default);

    /// <summary>Учётная запись (отслеживаемая, с сотрудником).</summary>
    Task<UserAccount?> GetAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken ct = default);

    Task<bool> LoginExistsAsync(string login, CancellationToken ct = default);

    /// <summary>Есть ли у сотрудника другая учётная запись.</summary>
    Task<bool> EmployeeHasAccountAsync(int employeeId, int? exceptUserId, CancellationToken ct = default);

    /// <summary>Сколько активных администраторов, не считая указанного.</summary>
    Task<int> CountActiveAdminsAsync(int? exceptUserId, CancellationToken ct = default);

    Task<Employee?> GetEmployeeAsync(int employeeId, CancellationToken ct = default);

    void Add(UserAccount user);

    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Вход и собственный пароль.</summary>
public interface IAuthService
{
    /// <summary>Проверяет логин и пароль. Неверные данные — <see cref="Domain.Exceptions.AuthenticationFailedException"/>.</summary>
    Task<CurrentUserDto> LoginAsync(LoginRequest request, CancellationToken ct = default);

    Task<CurrentUserDto> GetCurrentAsync(int userId, CancellationToken ct = default);

    Task ChangePasswordAsync(int userId, ChangePasswordRequest request, CancellationToken ct = default);

    /// <summary>
    /// Первый запуск: если учётных записей нет, создаёт администратора «admin»
    /// с указанным паролем (при первом входе его нужно сменить). Возвращает true, если создал.
    /// </summary>
    Task<bool> EnsureInitialAdminAsync(string password, CancellationToken ct = default);
}

/// <summary>Управление учётными записями (администратор).</summary>
public interface IUserService
{
    Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct = default);

    Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct = default);

    /// <param name="actingUserId">Кто изменяет — нельзя лишить прав администратора самого себя.</param>
    Task<UserDto> UpdateAsync(int id, UpdateUserRequest request, int actingUserId, CancellationToken ct = default);

    Task ResetPasswordAsync(int id, ResetPasswordRequest request, CancellationToken ct = default);
}
