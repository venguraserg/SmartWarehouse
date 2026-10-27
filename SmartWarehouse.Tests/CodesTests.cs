using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Web.Services;

namespace SmartWarehouse.Tests;

public class CodesTests
{
    [Theory]
    [InlineData("a-01", "A01")]
    [InlineData(" ск 1 ", "СК1")]
    [InlineData("A.1/2", "A12")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Ввод_приводится_к_допустимому_коду(string? input, string expected) =>
        Assert.Equal(expected, Codes.Normalize(input));

    [Fact]
    public void Длинный_код_обрезается() =>
        Assert.Equal(Codes.MaxLength, Codes.Normalize(new string('A', 30)).Length);

    [Fact]
    public void Стеллажи_получают_следующую_свободную_букву()
    {
        Assert.Equal("A", Codes.NextLocationCode(LocationType.Rack, []));
        Assert.Equal("C", Codes.NextLocationCode(LocationType.Rack, ["A", "B"]));
        Assert.Equal("B", Codes.NextLocationCode(LocationType.Zone, ["A", "C"]));
    }

    [Fact]
    public void Полки_и_ячейки_получают_следующий_номер()
    {
        Assert.Equal("01", Codes.NextLocationCode(LocationType.Cell, []));
        Assert.Equal("03", Codes.NextLocationCode(LocationType.Cell, ["01", "02"]));
        Assert.Equal("11", Codes.NextLocationCode(LocationType.Shelf, ["01", "10", "X"]));
        Assert.Equal("004", Codes.NextLocationCode(LocationType.Cell, ["001", "003"]));
    }

    [Fact]
    public void Когда_буквы_закончились_стеллажи_нумеруются()
    {
        var all = Enumerable.Range('A', 26).Select(c => ((char)c).ToString());
        Assert.Equal("01", Codes.NextLocationCode(LocationType.Rack, all));
    }

    [Fact]
    public void Следующий_код_склада_и_оборудования()
    {
        Assert.Equal("СК1", Codes.NextWarehouseCode([]));
        Assert.Equal("СК3", Codes.NextWarehouseCode(["СК1", "СК2", "ОСН"]));

        Assert.Equal("УЧ-01", Codes.NextEquipmentCode(null, []));
        Assert.Equal("L1-03", Codes.NextEquipmentCode("L1", ["L1", "L1-01", "L1-02", "УЧ-01"]));
    }
}
