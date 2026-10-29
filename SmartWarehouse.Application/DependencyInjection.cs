using Microsoft.Extensions.DependencyInjection;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Application.Services;

namespace SmartWarehouse.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<StockService>();
        services.AddScoped<IStockService>(sp => sp.GetRequiredService<StockService>());
        services.AddScoped<IWriteOffActService, WriteOffActService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<ILocationService, LocationService>();
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<IActService, ActService>();
        services.AddScoped<AccountService>();
        services.AddScoped<IAuthService>(sp => sp.GetRequiredService<AccountService>());
        services.AddScoped<IUserService>(sp => sp.GetRequiredService<AccountService>());
        return services;
    }
}
