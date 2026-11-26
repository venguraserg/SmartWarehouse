using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Web.Services;

namespace SmartWarehouse.Tests;

public class WebHelpersTests
{
    [Fact]
    public void Черновик_запроса_читается_и_пропускает_испорченное()
    {
        var lines = RequestCart.Parse("12:1; 40:2.5;bad;7:0;12:3;-1:1;9:x").ToList();
        Assert.Equal([new RequestCart.Line(12, 1), new RequestCart.Line(40, 2.5m)], lines);
        Assert.Empty(RequestCart.Parse(null));
    }

    [Fact]
    public void Дерево_оборудования_с_путём_и_потерянными_узлами()
    {
        var all = new List<EquipmentDto>
        {
            new(1, null, "УЧ-01", "Цех розлива"),
            new(2, 1, "УЧ-01-02", "Линия 2"),
            new(3, 1, "УЧ-01-01", "Линия 1"),
            new(4, 3, "УЧ-01-01-01", "Насос"),
            new(5, 99, "X-1", "Без родителя"),
        };

        var roots = EquipmentTree.Build(all);

        Assert.Equal(["УЧ-01", "X-1"], roots.Select(r => r.Item.Code));
        Assert.Equal(["УЧ-01-01", "УЧ-01-02"], roots[0].Children.Select(c => c.Item.Code));
        var pump = roots[0].Children[0].Children[0];
        Assert.Equal(["Цех розлива", "Линия 1", "Насос"], pump.Path().Select(n => n.Item.Name));
        Assert.Equal(3, roots[0].Descendants().Count());
    }

    [Theory]
    [InlineData(1, "позиция")]
    [InlineData(3, "позиции")]
    [InlineData(11, "позиций")]
    [InlineData(22, "позиции")]
    [InlineData(25, "позиций")]
    public void Число_и_форма_слова(int n, string expected) =>
        Assert.Equal(expected, Ui.Plural(n, "позиция", "позиции", "позиций"));

    // ---------- Настройка меню ----------

    private static System.Security.Claims.ClaimsPrincipal UserWith(params string[] roles) =>
        new(new System.Security.Claims.ClaimsIdentity(
            roles.Select(r => new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, r)), "test"));

    private static NavItem Nav(string key) => NavMenu.Items.Single(i => i.Key == key);

    [Fact]
    public void Меню_скрывает_только_видимость_но_не_даёт_прав()
    {
        var settings = new MenuSettingsDto(new Dictionary<string, IReadOnlyList<string>>
        {
            [AppRoles.Storekeeper] = ["reconcile", "inventory"],
            [AppRoles.Employee] = ["equipment"],
            [AppRoles.Admin] = ["admin-menu"] // нельзя скрыть у администратора
        });
        var storekeeper = UserWith(AppRoles.Employee, AppRoles.Storekeeper);

        Assert.False(NavMenu.Visible(settings, Nav("reconcile"), storekeeper));
        Assert.True(NavMenu.Visible(settings, Nav("receiving"), storekeeper));
        // Сотруднику скрыто, но кладовщику (вторая роль) — нет: виден.
        Assert.True(NavMenu.Visible(settings, Nav("equipment"), storekeeper));
        Assert.False(NavMenu.Visible(settings, Nav("equipment"), UserWith(AppRoles.Employee)));
        // Механику «Инвентаризация» не видна ни при каких настройках — нет прав.
        Assert.False(NavMenu.Visible(null, Nav("inventory"), UserWith(AppRoles.Employee)));
        Assert.True(NavMenu.Visible(settings, Nav("admin-menu"), UserWith(AppRoles.Admin)));
        // Ключи пунктов уникальны.
        Assert.Equal(NavMenu.Items.Count, NavMenu.Items.Select(i => i.Key).Distinct().Count());
    }
}
