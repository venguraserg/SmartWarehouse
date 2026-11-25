using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Domain.Enums;
using SmartWarehouse.Web.Services;

namespace SmartWarehouse.Tests;

public class PlaceAddressTests
{
    // Склад как после импорта начальных остатков: автоимена «1-й этаж», «Стеллаж C3», «Полка 1», «Ячейка 1».
    private static PlaceTree Tree()
    {
        var cell1 = new LocationNodeDto(5, 4, LocationType.Cell, "1", "SK1-1-C3-1-1", "Ячейка 1", []);
        var cell2 = new LocationNodeDto(6, 4, LocationType.Cell, "2", "SK1-1-C3-1-2", "Подшипники SKF", []);
        var shelf1 = new LocationNodeDto(4, 3, LocationType.Shelf, "1", "SK1-1-C3-1", "Полка 1", [cell1, cell2]);
        var floor = new LocationNodeDto(7, 3, LocationType.Shelf, "0", "SK1-1-C3-0", "Полка 0", []);
        var rack = new LocationNodeDto(3, 2, LocationType.Rack, "C3", "SK1-1-C3", "Стеллаж C3", [floor, shelf1]);
        var zone1 = new LocationNodeDto(2, null, LocationType.Zone, "1", "SK1-1", "1-й этаж", [rack]);
        var gateShelf = new LocationNodeDto(10, 9, LocationType.Shelf, "0", "SK1-V-34-0", "Полка 0", []);
        var gateRack = new LocationNodeDto(9, 8, LocationType.Rack, "34", "SK1-V-34", "Стеллаж 34", [gateShelf]);
        var gate = new LocationNodeDto(8, null, LocationType.Zone, "V", "SK1-V", "У ворот", [gateRack]);
        var sorting = new LocationNodeDto(11, null, LocationType.Zone, "RCV", "SK1-RCV", "Сортировка", []);
        return new PlaceTree().AddGroup(null, [zone1, gate, sorting]);
    }

    [Fact]
    public void Ячейка_словами_без_автоимён()
    {
        var a = Tree().AddressOf(5);
        Assert.Equal("1-й этаж", a.Area);
        Assert.Equal("Стеллаж C3 · полка 1 · ячейка 1", a.Full);
        Assert.Equal("C3 · п.1 · яч.1", a.Short);
        Assert.Equal("полка 1 · ячейка 1", a.Rest);
        Assert.Null(a.Note);
        Assert.Equal("SK1-1-C3-1-1", a.Code);
        Assert.Equal(3, a.GroupId);
        Assert.Equal("1-й этаж · Стеллаж C3", a.GroupTitle);
    }

    [Fact]
    public void Своё_описание_места_показывается_отдельно()
    {
        var a = Tree().AddressOf(6);
        Assert.Equal("Стеллаж C3 · полка 1 · ячейка 2", a.Full);
        Assert.Equal("Подшипники SKF", a.Note);
        Assert.Contains("Подшипники SKF", a.Title);
    }

    [Fact]
    public void Полка_0_это_пол()
    {
        var a = Tree().AddressOf(7);
        Assert.Equal("Стеллаж C3 · на полу", a.Full);
        Assert.Equal("C3 · пол", a.Short);

        var gate = Tree().AddressOf(10);
        Assert.Equal("У ворот", gate.Area);
        Assert.Equal("Стеллаж 34 · на полу", gate.Full);
        Assert.Equal("у ворот · 34 · пол", gate.Short);
    }

    [Fact]
    public void Стеллаж_и_зона_целиком()
    {
        var tree = Tree();
        var rack = tree.AddressOf(3);
        Assert.Equal("Стеллаж C3", rack.Full);
        Assert.Null(rack.Rest);
        Assert.Equal(3, rack.GroupId);

        var sorting = tree.AddressOf(11);
        Assert.Equal("Сортировка", sorting.Full);
        Assert.Equal("Сортировка", sorting.GroupTitle);
        Assert.Null(sorting.Note);
    }

    [Fact]
    public void Без_описаний_адрес_тот_же()
    {
        // После очистки автоимён у мест нет описаний — адрес и подписи строятся из типа и кода.
        var cell = new LocationNodeDto(5, 4, LocationType.Cell, "1", "SK1-1-C3-1-1", null, []);
        var floor = new LocationNodeDto(7, 3, LocationType.Shelf, "0", "SK1-1-C3-0", null, []);
        var shelf = new LocationNodeDto(4, 3, LocationType.Shelf, "1", "SK1-1-C3-1", null, [cell]);
        var rack = new LocationNodeDto(3, 2, LocationType.Rack, "C3", "SK1-1-C3", null, [floor, shelf]);
        var zone = new LocationNodeDto(2, null, LocationType.Zone, "1", "SK1-1", null, [rack]);
        var tree = new PlaceTree().AddGroup(null, [zone]);

        var a = tree.AddressOf(5);
        Assert.Equal("1-й этаж", a.Area);
        Assert.Equal("Стеллаж C3 · полка 1 · ячейка 1", a.Full);
        Assert.Null(a.Note);
        Assert.Equal("1-й этаж › Стеллаж C3 › Полка 1 › Ячейка 1", tree.PathOf(5));
        Assert.Equal("1-й этаж › Стеллаж C3 › На полу", tree.PathOf(7));
        Assert.Equal("Подшипники", PlaceTree.LabelOf(cell with { Name = " Подшипники " }));
        Assert.Equal("Ячейка 1", PlaceTree.LabelOf(cell with { Name = "Ячейка 1" }));
    }

    [Fact]
    public void Без_дерева_показывается_шифр()
    {
        var a = new PlaceTree().AddressOf(99, "SK1-1-A1-0-1", "Ячейка 1");
        Assert.Equal("SK1-1-A1-0-1", a.Full);
        Assert.Equal("SK1-1-A1-0-1", a.Short);
    }
}
