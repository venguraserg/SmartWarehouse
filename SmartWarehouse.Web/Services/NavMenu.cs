using System.Security.Claims;
using SmartWarehouse.Application.DTOs;

namespace SmartWarehouse.Web.Services;

/// <summary>
/// Пункт бокового меню. Roles — у кого есть доступ (пусто — у всех вошедших): это права, их меняет только код.
/// Администратор в «Меню» лишь скрывает пункты для ролей.
/// </summary>
public sealed record NavItem(string Key, string Group, string Title, string Href, string Icon, string[] Roles, bool Exact = false);

/// <summary>Пункты бокового меню — в порядке показа, по группам.</summary>
public static class NavMenu
{
    public const string Top = "";
    public const string Accounting = "Учёт";
    public const string Data = "Данные";
    public const string Admin = "Администрирование";

    private static readonly string[] All = [];
    private static readonly string[] Warehouse = [AppRoles.Storekeeper, AppRoles.Admin];
    private static readonly string[] Issuing = [AppRoles.Storekeeper, AppRoles.Admin, AppRoles.ShiftLead];
    private static readonly string[] Staff = [AppRoles.Admin, AppRoles.Storekeeper, AppRoles.Manager];
    private static readonly string[] AdminOnly = [AppRoles.Admin];

    public static readonly IReadOnlyList<NavItem> Items =
    [
        new("scan", Top, "Сканировать", "/scan", "scan", All),
        new("home", Top, "Обзор", "/", "home", All, Exact: true),

        new("stock", Accounting, "Остатки", "/stock", "box", All),
        new("requests", Accounting, "Запросы на выдачу", "/requests", "inbox", All),
        new("custody", Accounting, "Подотчёт", "/custody", "custody", All),
        new("reports", Accounting, "Отчёты об использовании", "/reports", "report", All),
        new("summary-acts", Accounting, "Сводные акты", "/summary-acts", "summary", Warehouse),
        new("acts", Accounting, "Акты на списание", "/acts", "act", All),
        new("documents", Accounting, "Документы", "/documents", "docs", All),
        new("receiving", Accounting, "Приёмка по накладной", "/receiving", "receive", Warehouse),
        new("receipt", Accounting, "Приход вручную", "/documents/new/Receipt", "plus", Warehouse),
        new("issue", Accounting, "Выдача сотруднику", "/documents/new/Issue", "issue", Issuing),
        new("return", Accounting, "Возврат", "/documents/new/Return", "return", Issuing),
        new("transfer", Accounting, "Перемещение", "/documents/new/Transfer", "transfer", Warehouse),

        new("items", Data, "Номенклатура", "/items", "tag", All),
        new("equipment", Data, "Оборудование", "/equipment", "equipment", All),
        new("warehouses", Data, "Склады и места", "/warehouses", "shelves", Warehouse),
        new("inventory", Data, "Инвентаризация", "/inventory", "check", Warehouse),
        new("employees", Data, "Сотрудники", "/employees", "users", Staff),
        new("references", Data, "Справочники", "/references", "book", Warehouse),
        new("labels", Data, "Наклейки QR", "/labels", "qr", Warehouse),
        new("reconcile", Data, "Сверка с 1С", "/reconcile", "sync", Warehouse),

        new("admin-users", Admin, "Пользователи", "/admin/users", "user", AdminOnly),
        new("admin-import", Admin, "Импорт остатков", "/admin/import", "upload", AdminOnly),
        new("admin-settings", Admin, "Настройки", "/admin/settings", "settings", AdminOnly),
        new("admin-menu", Admin, "Меню", "/admin/menu", "more", AdminOnly),
    ];

    /// <summary>Роли — столбцы настройки меню (подписи как в «Пользователях»).</summary>
    public static readonly IReadOnlyList<(string Role, string Title)> Roles =
    [
        (AppRoles.Employee, "Сотрудник"),
        (AppRoles.ShiftLead, "Старший смены"),
        (AppRoles.Manager, "Мастер"),
        (AppRoles.Storekeeper, "Кладовщик"),
        (AppRoles.Admin, "Администратор"),
    ];

    /// <summary>У роли есть доступ к пункту (по коду, не по настройке).</summary>
    public static bool Allowed(NavItem item, string role) => item.Roles.Length == 0 || item.Roles.Contains(role);

    /// <summary>Пункт у администратора скрыть нельзя.</summary>
    public static bool Required(NavItem item, string role) =>
        role == AppRoles.Admin && SmartWarehouse.Application.Services.SettingsService.AdminRequiredMenu.Contains(item.Key);

    public static bool Hidden(MenuSettingsDto? settings, NavItem item, string role) =>
        settings?.Hidden is { } hidden && hidden.TryGetValue(role, out var keys) && keys.Contains(item.Key) && !Required(item, role);

    /// <summary>
    /// Показывать пункт пользователю: хотя бы одна его роль даёт доступ и не скрывает пункт.
    /// Пользователь без ролей из списка (не должно быть) видит пункты, доступные всем.
    /// </summary>
    public static bool Visible(MenuSettingsDto? settings, NavItem item, ClaimsPrincipal user)
    {
        var roles = Roles.Select(r => r.Role).Where(user.IsInRole).ToList();
        if (roles.Count == 0)
            return item.Roles.Length == 0;
        return roles.Any(r => Allowed(item, r) && !Hidden(settings, item, r));
    }
}

/// <summary>
/// Настройка меню на весь сервер: загружается один раз, после сохранения администратором
/// сразу обновляет меню у всех открытых вкладок.
/// </summary>
public sealed class MenuState
{
    public MenuSettingsDto? Current { get; private set; }

    public event Action? Changed;

    public void Set(MenuSettingsDto settings)
    {
        Current = settings;
        Changed?.Invoke();
    }
}
