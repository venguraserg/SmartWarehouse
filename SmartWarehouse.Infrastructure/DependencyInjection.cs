using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Infrastructure.Pdf;
using SmartWarehouse.Infrastructure.Security;
using SmartWarehouse.Infrastructure.Data;
using SmartWarehouse.Infrastructure.Repositories;

namespace SmartWarehouse.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<WarehouseDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IStockRepository, StockRepository>();
        services.AddScoped<ICatalogRepository, CatalogRepository>();
        services.AddScoped<ILocationRepository, LocationRepository>();
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<IWriteOffActRepository, WriteOffActRepository>();
        services.AddSingleton<IActPdfRenderer, ActPdfRenderer>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddSingleton<SmartWarehouse.Application.Interfaces.IPasswordHasher, IdentityPasswordHasher>();
        return services;
    }
}
