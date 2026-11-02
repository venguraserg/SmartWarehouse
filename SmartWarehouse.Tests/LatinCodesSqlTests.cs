using SmartWarehouse.Infrastructure.Data;

namespace SmartWarehouse.Tests;

public class LatinCodesSqlTests
{
    [Fact]
    public void SQL_перевода_кодов_строится()
    {
        var sql = LatinCodesSql.Build();

        Assert.Contains("UPDATE warehouses", sql);
        Assert.Contains("UPDATE storage_locations", sql);
        Assert.Contains("UPDATE items", sql);
        Assert.Contains("'зч-'", sql);
        Assert.Contains("THEN 'SP-'", sql);
        Assert.Contains("replace(", sql);
        Assert.Equal(0, sql.Count(c => c == '\'') % 2); // кавычки парные — строки SQL не сломаны
    }
}
