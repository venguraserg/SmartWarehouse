using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.Web.Services;

/// <summary>Ошибка, которую вернул API (текст уже понятен пользователю).</summary>
public class ApiException(string message, HttpStatusCode status) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>Типизированный клиент REST API склада.</summary>
public class ApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    // ---------- Справочники ----------

    public Task<List<CategoryDto>> GetCategoriesAsync(CancellationToken ct = default) =>
        GetAsync<List<CategoryDto>>("api/categories", ct);

    public Task<CategoryDto> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken ct = default) =>
        SendAsync<CategoryDto>(HttpMethod.Post, "api/categories", request, ct);

    public Task<List<UnitDto>> GetUnitsAsync(CancellationToken ct = default) =>
        GetAsync<List<UnitDto>>("api/units", ct);

    public Task<UnitDto> CreateUnitAsync(CreateUnitRequest request, CancellationToken ct = default) =>
        SendAsync<UnitDto>(HttpMethod.Post, "api/units", request, ct);

    public Task<List<EquipmentDto>> GetEquipmentAsync(CancellationToken ct = default) =>
        GetAsync<List<EquipmentDto>>("api/equipment", ct);

    public Task<EquipmentDto> CreateEquipmentAsync(CreateEquipmentRequest request, CancellationToken ct = default) =>
        SendAsync<EquipmentDto>(HttpMethod.Post, "api/equipment", request, ct);

    // ---------- Сотрудники ----------

    /// <summary>Все сотрудники, включая неработающих (фильтрация и поиск — на странице).</summary>
    public Task<List<EmployeeDto>> GetEmployeesAsync(bool includeInactive = true, CancellationToken ct = default) =>
        GetAsync<List<EmployeeDto>>($"api/employees?includeInactive={(includeInactive ? "true" : "false")}", ct);

    public Task<EmployeeDto> GetEmployeeAsync(int id, CancellationToken ct = default) =>
        GetAsync<EmployeeDto>($"api/employees/{id}", ct);

    public Task<EmployeeDto> CreateEmployeeAsync(SaveEmployeeRequest request, CancellationToken ct = default) =>
        SendAsync<EmployeeDto>(HttpMethod.Post, "api/employees", request, ct);

    public Task<EmployeeDto> UpdateEmployeeAsync(int id, SaveEmployeeRequest request, CancellationToken ct = default) =>
        SendAsync<EmployeeDto>(HttpMethod.Put, $"api/employees/{id}", request, ct);

    public Task<List<EmployeeMovementDto>> GetEmployeeHistoryAsync(int id, int take = 200, CancellationToken ct = default) =>
        GetAsync<List<EmployeeMovementDto>>($"api/employees/{id}/history?take={take}", ct);

    /// <summary>Что числится за сотрудниками (подотчёт). Фильтры необязательны.</summary>
    public Task<List<CustodyRowDto>> GetCustodyAsync(int? employeeId = null, int? itemId = null, string? search = null, CancellationToken ct = default)
    {
        var parts = new List<string>();
        if (employeeId is not null) parts.Add($"employeeId={employeeId}");
        if (itemId is not null) parts.Add($"itemId={itemId}");
        if (!string.IsNullOrWhiteSpace(search)) parts.Add($"search={Uri.EscapeDataString(search.Trim())}");
        return GetAsync<List<CustodyRowDto>>(parts.Count == 0 ? "api/custody" : $"api/custody?{string.Join('&', parts)}", ct);
    }

    // ---------- Номенклатура ----------

    public Task<List<ItemDto>> GetItemsAsync(string? search = null, int? categoryId = null, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) query.Add($"search={Uri.EscapeDataString(search.Trim())}");
        if (categoryId is not null) query.Add($"categoryId={categoryId}");
        var url = query.Count == 0 ? "api/items" : $"api/items?{string.Join('&', query)}";
        return GetAsync<List<ItemDto>>(url, ct);
    }

    public Task<ItemDto> GetItemAsync(int id, CancellationToken ct = default) =>
        GetAsync<ItemDto>($"api/items/{id}", ct);

    public Task<ItemDto> CreateItemAsync(CreateItemRequest request, CancellationToken ct = default) =>
        SendAsync<ItemDto>(HttpMethod.Post, "api/items", request, ct);

    public Task<ItemDto> UpdateItemAsync(int id, UpdateItemRequest request, CancellationToken ct = default) =>
        SendAsync<ItemDto>(HttpMethod.Put, $"api/items/{id}", request, ct);

    /// <summary>Следующий свободный артикул вида «ЗЧ-000001».</summary>
    public async Task<string> GetNextSkuAsync(CancellationToken ct = default) =>
        (await GetAsync<NextSkuBody>("api/items/next-sku", ct)).Sku;

    public Task<List<StockBalanceDto>> GetBalancesAsync(int itemId, CancellationToken ct = default) =>
        GetAsync<List<StockBalanceDto>>($"api/items/{itemId}/balances", ct);

    public Task<List<MovementDto>> GetMovementsAsync(int itemId, int take = 100, CancellationToken ct = default) =>
        GetAsync<List<MovementDto>>($"api/items/{itemId}/movements?take={take}", ct);

    public async Task<ItemDto> UploadItemImageAsync(int itemId, byte[] data, string fileName, string contentType, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = MediaTypeHeaderValue.TryParse(contentType, out var type)
            ? type
            : new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "file", fileName);

        using var response = await http.PutAsync($"api/items/{itemId}/image", content, ct);
        return await ReadAsync<ItemDto>(response, ct);
    }

    public async Task DeleteItemImageAsync(int itemId, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"api/items/{itemId}/image", ct);
        await EnsureSuccessAsync(response, ct);
    }

    /// <summary>Фото позиции или null, если его нет.</summary>
    public async Task<ItemImageDto?> GetItemImageAsync(int itemId, CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"api/items/{itemId}/image", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        await EnsureSuccessAsync(response, ct);
        var data = await response.Content.ReadAsByteArrayAsync(ct);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        return new ItemImageDto(data, contentType);
    }

    // ---------- Склады и места хранения ----------

    public Task<List<WarehouseDto>> GetWarehousesAsync(CancellationToken ct = default) =>
        GetAsync<List<WarehouseDto>>("api/warehouses", ct);

    public Task<WarehouseDto> CreateWarehouseAsync(CreateWarehouseRequest request, CancellationToken ct = default) =>
        SendAsync<WarehouseDto>(HttpMethod.Post, "api/warehouses", request, ct);

    public Task<List<LocationNodeDto>> GetLocationTreeAsync(int warehouseId, CancellationToken ct = default) =>
        GetAsync<List<LocationNodeDto>>($"api/warehouses/{warehouseId}/locations", ct);

    public Task<LocationDto> AddLocationAsync(int warehouseId, CreateLocationRequest request, CancellationToken ct = default) =>
        SendAsync<LocationDto>(HttpMethod.Post, $"api/warehouses/{warehouseId}/locations", request, ct);

    public Task<List<LocationContentDto>> GetLocationContentAsync(int locationId, CancellationToken ct = default) =>
        GetAsync<List<LocationContentDto>>($"api/locations/{locationId}/content", ct);

    public Task<LocationDto> GetLocationByCodeAsync(string fullCode, CancellationToken ct = default) =>
        GetAsync<LocationDto>($"api/locations/by-code/{Uri.EscapeDataString(fullCode.Trim())}", ct);

    /// <summary>Все конечные места всех складов (куда можно класть товар) — для выпадающих списков.</summary>
    public async Task<List<LocationNodeDto>> GetLeafLocationsAsync(CancellationToken ct = default)
    {
        var result = new List<LocationNodeDto>();
        foreach (var warehouse in await GetWarehousesAsync(ct))
        {
            var tree = await GetLocationTreeAsync(warehouse.Id, ct);
            CollectLeaves(tree, result);
        }
        return result.OrderBy(l => l.FullCode).ToList();

        static void CollectLeaves(IEnumerable<LocationNodeDto> nodes, List<LocationNodeDto> into)
        {
            foreach (var node in nodes)
            {
                if (node.Children.Count == 0) into.Add(node);
                else CollectLeaves(node.Children, into);
            }
        }
    }

    // ---------- Документы и остатки ----------

    public Task<List<DocumentSummaryDto>> GetDocumentsAsync(MovementType? type = null, int take = 100, CancellationToken ct = default) =>
        GetAsync<List<DocumentSummaryDto>>(type is null ? $"api/documents?take={take}" : $"api/documents?type={type}&take={take}", ct);

    public Task<DocumentDto> GetDocumentAsync(long id, CancellationToken ct = default) =>
        GetAsync<DocumentDto>($"api/documents/{id}", ct);

    /// <summary>Акт на списание (PDF).</summary>
    public async Task<byte[]> GetWriteOffActAsync(long documentId, CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"api/documents/{documentId}/act", ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public Task<DocumentDto> PostDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default) =>
        SendAsync<DocumentDto>(HttpMethod.Post, "api/documents", request, ct);

    public Task<StockSearchResult> SearchStockAsync(StockQuery query, CancellationToken ct = default)
    {
        var parts = new List<string>();
        if (query.WarehouseId is not null) parts.Add($"warehouseId={query.WarehouseId}");
        if (!string.IsNullOrWhiteSpace(query.Location)) parts.Add($"location={Uri.EscapeDataString(query.Location.Trim())}");
        if (!string.IsNullOrWhiteSpace(query.Search)) parts.Add($"search={Uri.EscapeDataString(query.Search.Trim())}");
        if (query.CategoryId is not null) parts.Add($"categoryId={query.CategoryId}");
        if (query.BelowMinimum) parts.Add("belowMinimum=true");

        return GetAsync<StockSearchResult>(parts.Count == 0 ? "api/stock" : $"api/stock?{string.Join('&', parts)}", ct);
    }

    /// <summary>Все места всех складов (любого уровня) — для подсказок в фильтре.</summary>
    public async Task<List<LocationNodeDto>> GetAllLocationsAsync(CancellationToken ct = default)
    {
        var result = new List<LocationNodeDto>();
        foreach (var warehouse in await GetWarehousesAsync(ct))
            Collect(await GetLocationTreeAsync(warehouse.Id, ct), result);
        return result.OrderBy(l => l.FullCode).ToList();

        static void Collect(IEnumerable<LocationNodeDto> nodes, List<LocationNodeDto> into)
        {
            foreach (var node in nodes)
            {
                into.Add(node);
                Collect(node.Children, into);
            }
        }
    }

    public Task<List<LowStockItemDto>> GetBelowMinimumAsync(CancellationToken ct = default) =>
        GetAsync<List<LowStockItemDto>>("api/stock/below-minimum", ct);

    // ---------- Общее ----------

    private async Task<T> GetAsync<T>(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        return await ReadAsync<T>(response, ct);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body, options: Json) };
        using var response = await http.SendAsync(request, ct);
        return await ReadAsync<T>(response, ct);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct)
            ?? throw new ApiException("Пустой ответ сервера.", response.StatusCode);
    }

    /// <summary>При ошибке читает ProblemDetails из ответа API и бросает исключение с понятным текстом.</summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        string? message = null;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(Json, ct);
            message = problem?.Detail ?? problem?.Title;
        }
        catch (JsonException)
        {
            // ответ не в формате ProblemDetails
        }

        throw new ApiException(message ?? $"Ошибка сервера ({(int)response.StatusCode}).", response.StatusCode);
    }

    private sealed record ProblemBody(string? Title, string? Detail);

    private sealed record NextSkuBody(string Sku);
}
