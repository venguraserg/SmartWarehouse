using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using SmartWarehouse.API.Infrastructure;
using SmartWarehouse.API.Security;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Application;
using SmartWarehouse.Application.Services;
using SmartWarehouse.Infrastructure;
using SmartWarehouse.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// Строка подключения не хранится в репозитории: задаётся через user-secrets (разработка)
// или переменную окружения ConnectionStrings__Default (сервер, Docker).
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "Не задана строка подключения ConnectionStrings:Default. См. README, раздел «Запуск».");

builder.Services.AddApplication();
// Наименование организации / подразделения в шапке актов (необязательно).
builder.Services.AddSingleton(new DocumentPrintOptions(builder.Configuration["Documents:Organization"]));
builder.Services.AddInfrastructure(connectionString);

// ---------- Вход по токену (JWT) ----------
using (var loggerFactory = LoggerFactory.Create(b => b.AddConsole()))
{
    var jwt = JwtOptions.FromConfiguration(builder.Configuration, builder.Environment.IsDevelopment(), loggerFactory.CreateLogger("Auth"));
    builder.Services.AddSingleton(jwt);
}
builder.Services.AddSingleton<TokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<JwtOptions>((options, jwt) =>
    {
        options.MapInboundClaims = false; // имена утверждений как в токене
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = JwtOptions.Issuer,
            ValidAudience = JwtOptions.Audience,
            IssuerSigningKey = jwt.Key,
            NameClaimType = System.Security.Claims.ClaimTypes.Name,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

// По умолчанию все запросы — только для вошедших; исключения помечены [AllowAnonymous].
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

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

// Первый запуск: создаём администратора, если учётных записей ещё нет.
{
    using var scope = app.Services.CreateScope();
    var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
    var password = app.Configuration["Auth:InitialAdminPassword"] is { Length: > 0 } configured ? configured : "admin123";
    try
    {
        if (await auth.EnsureInitialAdminAsync(password))
            app.Logger.LogWarning("Создан администратор «admin» с начальным паролем из Auth:InitialAdminPassword (по умолчанию admin123). Смените его при первом входе.");
    }
    catch (Exception ex)
    {
        // Например, база ещё не создана (миграции не применены) — API всё равно запускается.
        app.Logger.LogError(ex, "Не удалось проверить учётную запись администратора.");
    }
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
