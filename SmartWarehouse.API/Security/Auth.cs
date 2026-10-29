using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.API.Security;

/// <summary>Имена ролей и сочетания для атрибутов [Authorize(Roles = ...)].</summary>
public static class Roles
{
    public const string Employee = nameof(UserRole.Employee);
    public const string Manager = nameof(UserRole.Manager);
    public const string Storekeeper = nameof(UserRole.Storekeeper);
    public const string Admin = nameof(UserRole.Admin);

    /// <summary>Складские операции и справочники склада.</summary>
    public const string Warehouse = Storekeeper + "," + Admin;
}

/// <summary>Имена собственных утверждений (claims) в токене.</summary>
public static class AppClaims
{
    public const string EmployeeId = "employee_id";
    public const string DisplayName = "display_name";
    public const string MustChangePassword = "must_change_password";
}

/// <summary>Настройки токенов.</summary>
public class JwtOptions
{
    public const string Issuer = "SmartWarehouse";
    public const string Audience = "SmartWarehouse";

    public required SymmetricSecurityKey Key { get; init; }

    public TimeSpan Lifetime { get; init; } = TimeSpan.FromHours(12);

    /// <summary>Постоянный ключ для разработки: вход не сбрасывается при перезапуске API из Visual Studio.</summary>
    private const string DevelopmentKey = "SmartWarehouse-development-only-key-do-not-use-in-production";

    /// <summary>
    /// Ключ подписи из настройки Auth:JwtKey (не короче 32 символов). Если не задан:
    /// в разработке — постоянный ключ разработки; иначе — случайный ключ на время работы процесса
    /// (после перезапуска API всем придётся войти заново).
    /// </summary>
    public static JwtOptions FromConfiguration(IConfiguration config, bool isDevelopment, ILogger logger)
    {
        var configured = config["Auth:JwtKey"];
        if (string.IsNullOrWhiteSpace(configured) && isDevelopment)
            configured = DevelopmentKey;

        byte[] keyBytes;
        if (string.IsNullOrWhiteSpace(configured))
        {
            logger.LogWarning("Не задан Auth:JwtKey — используется временный ключ, после перезапуска API потребуется повторный вход.");
            keyBytes = RandomNumberGenerator.GetBytes(64);
        }
        else
        {
            keyBytes = Encoding.UTF8.GetBytes(configured);
            if (keyBytes.Length < 32)
                throw new InvalidOperationException("Auth:JwtKey должен быть не короче 32 символов.");
        }

        var hours = config.GetValue<double?>("Auth:TokenHours") ?? 12;
        return new JwtOptions { Key = new SymmetricSecurityKey(keyBytes), Lifetime = TimeSpan.FromHours(hours) };
    }
}

/// <summary>Выпуск токена доступа для вошедшего пользователя.</summary>
public class TokenService(JwtOptions options, TimeProvider clock)
{
    public LoginResponse Create(CurrentUserDto user)
    {
        var now = clock.GetUtcNow();
        var expires = now + options.Lifetime;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Login),
            new(AppClaims.DisplayName, user.DisplayName),
            new(AppClaims.MustChangePassword, user.MustChangePassword ? "true" : "false")
        };
        if (user.EmployeeId is int employeeId)
            claims.Add(new Claim(AppClaims.EmployeeId, employeeId.ToString()));
        foreach (var role in Enum.GetValues<UserRole>().Where(r => r != UserRole.None && user.Roles.HasFlag(r)))
            claims.Add(new Claim(ClaimTypes.Role, role.ToString()));

        var token = new JwtSecurityToken(
            issuer: JwtOptions.Issuer,
            audience: JwtOptions.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(options.Key, SecurityAlgorithms.HmacSha256));

        return new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), expires, user);
    }
}

/// <summary>Текущий пользователь запроса (из токена).</summary>
public static class CurrentUserExtensions
{
    public static int UserId(this ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Нет пользователя."));

    public static int? EmployeeId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(AppClaims.EmployeeId), out var id) ? id : null;

    /// <summary>Видит весь подотчёт: кладовщик и администратор.</summary>
    public static bool SeesAllCustody(this ClaimsPrincipal user) =>
        user.IsInRole(Roles.Storekeeper) || user.IsInRole(Roles.Admin);
}
