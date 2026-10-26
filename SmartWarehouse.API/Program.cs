using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SmartWarehouse.API.Infrastructure;
using SmartWarehouse.Application;
using SmartWarehouse.Infrastructure;
using SmartWarehouse.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// Строка подключения не хранится в репозитории: задаётся через user-secrets (разработка)
// или переменную окружения ConnectionStrings__Default (сервер, Docker).
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "Не задана строка подключения ConnectionStrings:Default. См. README, раздел «Запуск».");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(connectionString);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();

var app = builder.Build();

// Применение миграций и начальное заполнение справочников при старте
// (включается через Database:MigrateOnStartup = true в user-secrets или переменной окружения).
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
