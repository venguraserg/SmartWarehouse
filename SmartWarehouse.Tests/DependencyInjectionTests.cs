using Microsoft.Extensions.DependencyInjection;
using SmartWarehouse.Application;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Infrastructure;

namespace SmartWarehouse.Tests;

/// <summary>
/// Все сервисы приложения собираются так же, как на сервере: зависимости обязательные,
/// и забытая регистрация упадёт здесь, а не «тихо отключит» функцию в работе.
/// </summary>
public class DependencyInjectionTests
{
    [Fact]
    public void Все_сервисы_приложения_собираются()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddSingleton(new DocumentPrintOptions(null));
        // Подключение к базе не открывается: контекст создаётся, но запросов нет.
        services.AddInfrastructure("Host=localhost;Database=none;Username=none;Password=none");

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();
        var ours = services
            .Where(d => !d.ServiceType.IsGenericTypeDefinition && d.ServiceType.Namespace?.StartsWith("SmartWarehouse", StringComparison.Ordinal) == true)
            .Select(d => d.ServiceType)
            .Distinct()
            .ToList();

        Assert.NotEmpty(ours);
        foreach (var type in ours)
            Assert.NotNull(scope.ServiceProvider.GetRequiredService(type));
    }
}
