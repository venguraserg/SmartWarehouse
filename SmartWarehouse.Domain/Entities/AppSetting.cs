namespace SmartWarehouse.Domain.Entities;

/// <summary>
/// Настройка приложения, которую меняет администратор (наклейки, шаблон акта и т.п.).
/// Значение — JSON раздела настроек целиком.
/// </summary>
public class AppSetting
{
    public string Key { get; set; } = "";

    public string Value { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Кто изменил (текстом).</summary>
    public string? UpdatedBy { get; set; }
}
