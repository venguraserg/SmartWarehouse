using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.Web.Services;

/// <summary>Роли для атрибутов и AuthorizeView (совпадают с именами UserRole).</summary>
public static class AppRoles
{
    public const string Employee = nameof(UserRole.Employee);
    public const string Manager = nameof(UserRole.Manager);
    public const string Storekeeper = nameof(UserRole.Storekeeper);
    public const string Admin = nameof(UserRole.Admin);

    /// <summary>Складские операции и справочники склада.</summary>
    public const string Warehouse = Storekeeper + "," + Admin;

    public static string Title(UserRole role) => role switch
    {
        UserRole.Employee => "Сотрудник",
        UserRole.Manager => "Руководитель",
        UserRole.Storekeeper => "Кладовщик",
        UserRole.Admin => "Администратор",
        _ => role.ToString()
    };
}

/// <summary>
/// Что хранится в cookie входа: сведения о пользователе и токен API.
/// Cookie зашифрована ASP.NET Core Data Protection — прочитать или подделать её в браузере нельзя.
/// </summary>
public static class AuthClaims
{
    public const string AccessToken = "access_token";
    public const string DisplayName = "display_name";
    public const string EmployeeId = "employee_id";
    public const string MustChangePassword = "must_change_password";

    public static ClaimsPrincipal CreatePrincipal(LoginResponse login)
    {
        var user = login.User;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Login),
            new(DisplayName, user.DisplayName),
            new(AccessToken, login.Token),
            new(MustChangePassword, user.MustChangePassword ? "true" : "false")
        };
        if (user.EmployeeId is int employeeId)
            claims.Add(new Claim(EmployeeId, employeeId.ToString()));
        foreach (var role in Enum.GetValues<UserRole>().Where(r => r != UserRole.None && user.Roles.HasFlag(r)))
            claims.Add(new Claim(ClaimTypes.Role, role.ToString()));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme, ClaimTypes.Name, ClaimTypes.Role);
        return new ClaimsPrincipal(identity);
    }

    public static string DisplayNameOf(this ClaimsPrincipal user) =>
        user.FindFirstValue(DisplayName) ?? user.Identity?.Name ?? "";

    public static int? EmployeeIdOf(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(EmployeeId), out var id) ? id : null;

    public static bool MustChangePasswordOf(this ClaimsPrincipal user) =>
        user.FindFirstValue(MustChangePassword) == "true";

    public static bool IsWarehouseStaff(this ClaimsPrincipal user) =>
        user.IsInRole(AppRoles.Storekeeper) || user.IsInRole(AppRoles.Admin);
}
