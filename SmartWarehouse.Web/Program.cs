using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using SmartWarehouse.Web.Components;
using SmartWarehouse.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Адрес API: в разработке — http://localhost:5113 (appsettings.json),
// в Docker — переменная окружения Api__BaseUrl=http://api:8080.
var apiBaseUrl = builder.Configuration["Api:BaseUrl"]
    ?? throw new InvalidOperationException("Не задан адрес API (Api:BaseUrl).");

builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<ApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddScoped<BrowserStore>();
builder.Services.AddScoped<ScannerHub>();

// Подсветка давнего подотчёта (Custody:AgeWarningDays, по умолчанию 30 дней).
builder.Services.AddSingleton(builder.Configuration.GetSection("Custody").Get<CustodyOptions>() ?? new CustodyOptions());

// ---------- Вход: зашифрованная cookie с токеном API ----------
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "sw.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/access-denied";
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = false; // срок как у токена API
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// Ключи шифрования cookie: на сервере храним в папке (DataProtection:KeysPath),
// иначе после перезапуска контейнера всем пришлось бы входить заново.
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("SmartWarehouse.Web")
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets();

// Фото номенклатуры отдаётся через веб-приложение: браузеру не нужен прямой доступ к API.
app.MapGet("/item-image/{id:int}", async (int id, ApiClient api, CancellationToken ct) =>
{
    var image = await api.GetItemImageAsync(id, ct);
    return image is null ? Results.NotFound() : Results.File(image.Data, image.ContentType);
}).RequireAuthorization();

// Акт на списание открывается в браузере как PDF (печать или «Сохранить как PDF»).
app.MapGet("/document-act/{id:long}", async (long id, ApiClient api, CancellationToken ct) =>
{
    try
    {
        var pdf = await api.GetWriteOffActAsync(id, ct);
        return Results.File(pdf, "application/pdf");
    }
    catch (ApiException ex)
    {
        return Results.Text(ex.Message, "text/plain; charset=utf-8", statusCode: (int)ex.Status);
    }
}).RequireAuthorization();

// Акт на списание по номеру акта (черновик — с надписью «ЧЕРНОВИК»).
app.MapGet("/act-pdf/{id:int}", async (int id, ApiClient api, CancellationToken ct) =>
{
    try
    {
        var pdf = await api.GetActPdfAsync(id, ct);
        return Results.File(pdf, "application/pdf");
    }
    catch (ApiException ex)
    {
        return Results.Text(ex.Message, "text/plain; charset=utf-8", statusCode: (int)ex.Status);
    }
}).RequireAuthorization();

// Наклейки с QR-кодами (PDF). В QR — ссылка на это веб-приложение: App:PublicUrl из настроек,
// а если не задан — адрес, по которому открыт сайт (за прокси https — см. ASPNETCORE_FORWARDEDHEADERS_ENABLED).
app.MapGet("/labels.pdf", async (HttpContext context, ApiClient api, IConfiguration config, CancellationToken ct) =>
{
    var q = context.Request.Query;
    var kind = Enum.TryParse<SmartWarehouse.Application.DTOs.LabelKind>(q["kind"], ignoreCase: true, out var k) ? k : SmartWarehouse.Application.DTOs.LabelKind.Location;
    var ids = (q["ids"].ToString() ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(s => int.TryParse(s, out var id) ? id : 0)
        .Where(id => id > 0)
        .ToList();
    var publicUrl = config["App:PublicUrl"];
    var baseUrl = string.IsNullOrWhiteSpace(publicUrl)
        ? $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}"
        : publicUrl;

    try
    {
        var pdf = await api.GetLabelsPdfAsync(new SmartWarehouse.Application.DTOs.LabelRequest(
            kind, ids, q["layout"],
            int.TryParse(q["skip"], out var skip) ? skip : 0,
            baseUrl,
            int.TryParse(q["copies"], out var copies) ? copies : 1), ct);
        return Results.File(pdf, "application/pdf");
    }
    catch (ApiException ex)
    {
        return Results.Text(ex.Message, "text/plain; charset=utf-8", statusCode: (int)ex.Status);
    }
}).RequireAuthorization();

// Выход.
app.MapPost("/account/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("/login");
});

// Сессия истекла (API ответил 401) — выходим и показываем страницу входа с пояснением.
app.MapGet("/account/logout-expired", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("/login?expired=1");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
