using Microsoft.Extensions.DependencyInjection;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Application.Services;

namespace SmartWarehouse.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IStockService, StockService>();
        services.AddScoped<ICatalogService, CatalogService>();
        return services;
    }
}
