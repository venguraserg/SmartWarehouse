using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.Domain.Entities;

/// <summary>Учётная запись для входа в программу. Обычно привязана к сотруднику.</summary>
public class UserAccount
{
    public int Id { get; set; }

    /// <summary>Логин (хранится в нижнем регистре, уникален).</summary>
    public string Login { get; set; } = string.Empty;

    /// <summary>Хеш пароля (сам пароль нигде не хранится).</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Сотрудник, от имени которого работает пользователь (для администратора может отсутствовать).</summary>
    public int? EmployeeId { get; set; }

    public Employee? Employee { get; set; }

    public UserRole Roles { get; set; }

    /// <summary>Заблокированная учётная запись не может войти.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Пароль выдан администратором — при входе нужно сменить на свой.</summary>
    public bool MustChangePassword { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    public bool Has(UserRole role) => (Roles & role) == role;
}
