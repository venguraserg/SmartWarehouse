using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartWarehouse.API.Security;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;

namespace SmartWarehouse.API.Controllers;

/// <summary>Вход и собственный пароль.</summary>
[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService auth, TokenService tokens) : ControllerBase
{
    /// <summary>Вход по логину и паролю. Возвращает токен для заголовка Authorization: Bearer.</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<LoginResponse> Login(LoginRequest request, CancellationToken ct) =>
        tokens.Create(await auth.LoginAsync(request, ct));

    /// <summary>Кто я.</summary>
    [HttpGet("me")]
    public Task<CurrentUserDto> Me(CancellationToken ct) => auth.GetCurrentAsync(User.UserId(), ct);

    /// <summary>Сменить свой пароль.</summary>
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await auth.ChangePasswordAsync(User.UserId(), request, ct);
        return NoContent();
    }
}

/// <summary>Учётные записи (только администратор).</summary>
[ApiController]
[Route("api/users")]
[Authorize(Roles = Roles.Admin)]
public class UsersController(IUserService users) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<UserDto>> List(CancellationToken ct) => users.ListAsync(ct);

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await users.CreateAsync(request, ct));

    [HttpPut("{id:int}")]
    public Task<UserDto> Update(int id, UpdateUserRequest request, CancellationToken ct) =>
        users.UpdateAsync(id, request, User.UserId(), ct);

    /// <summary>Задать временный пароль (при входе пользователь сменит его).</summary>
    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, ResetPasswordRequest request, CancellationToken ct)
    {
        await users.ResetPasswordAsync(id, request, ct);
        return NoContent();
    }
}
