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
}
