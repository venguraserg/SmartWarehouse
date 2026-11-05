using SmartWarehouse.Web.Services;

namespace SmartWarehouse.Tests;

public class CookieNamesTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("http://0.0.0.0:80", "")]
    [InlineData("http://0.0.0.0", "")]
    [InlineData("http://0.0.0.0:8200", ".8200")]
    [InlineData("http://+:8104/", ".8104")]
    [InlineData("http://*:5080;https://*:5443", ".5080")]
    public void Суффикс_по_порту(string? urls, string expected) =>
        Assert.Equal(expected, CookieNames.PortSuffix(urls));
}
