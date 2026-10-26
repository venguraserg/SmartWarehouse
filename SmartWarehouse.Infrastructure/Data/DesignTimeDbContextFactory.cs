using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartWarehouse.Infrastructure.Data;

/// <summary>
/// Используется только командами <c>dotnet ef</c> (создание миграций), чтобы им не требовалась
/// настроенная строка подключения API. Строку можно переопределить переменной SMARTWAREHOUSE_DESIGN_DB.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<WarehouseDbContext>
{
    public WarehouseDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SMARTWAREHOUSE_DESIGN_DB")
            ?? "Host=localhost;Database=smartwarehouse;Username=postgres";

        var options = new DbContextOptionsBuilder<WarehouseDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new WarehouseDbContext(options);
    }
}
