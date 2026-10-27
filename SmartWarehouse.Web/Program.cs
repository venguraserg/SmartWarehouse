using SmartWarehouse.Web.Components;
using SmartWarehouse.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Адрес API: в разработке — http://localhost:5113 (appsettings.json),
// в Docker — переменная окружения Api__BaseUrl=http://api:8080.
var apiBaseUrl = builder.Configuration["Api:BaseUrl"]
    ?? throw new InvalidOperationException("Не задан адрес API (Api:BaseUrl).");

builder.Services.AddHttpClient<ApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddScoped<BrowserStore>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
}

app.UseAntiforgery();
app.MapStaticAssets();

// Фото номенклатуры отдаётся через веб-приложение: браузеру не нужен прямой доступ к API.
app.MapGet("/item-image/{id:int}", async (int id, ApiClient api, CancellationToken ct) =>
{
    var image = await api.GetItemImageAsync(id, ct);
    return image is null ? Results.NotFound() : Results.File(image.Data, image.ContentType);
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
