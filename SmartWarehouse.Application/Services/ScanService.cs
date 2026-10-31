using SmartWarehouse.Application.DTOs;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Application.Services;

public interface IScanService
{
    /// <summary>
    /// Распознаёт отсканированный код: ссылку с нашей наклейки, код места хранения, артикул или заводской штрих-код.
    /// </summary>
    Task<ScanResultDto> ResolveAsync(string? code, CancellationToken ct = default);
}

public class ScanService(ILocationRepository locations, ICatalogRepository catalog) : IScanService
{
    public const int MaxCodeLength = 512;

    public async Task<ScanResultDto> ResolveAsync(string? code, CancellationToken ct = default)
    {
        var raw = code?.Trim();
        if (string.IsNullOrEmpty(raw))
            throw new DomainException("Пустой код.");
        if (raw.Length > MaxCodeLength)
            throw new DomainException("Слишком длинный код.");

        // Ссылка с нашей наклейки: …/l/<код места> или …/i/<артикул>.
        if (LabelLinks.TryParse(raw, out var kind, out var value))
        {
            var found = kind == ScanKind.Location
                ? await FindLocationAsync(value, ct)
                : await FindItemAsync(value, ct);
            return found ?? throw new NotFoundException(kind == ScanKind.Location
                ? $"Место хранения «{value}» не найдено (наклейка от удалённого или переименованного места?)."
                : $"Позиция «{value}» не найдена.");
        }

        return await FindLocationAsync(raw, ct)
            ?? await FindItemAsync(raw, ct)
            ?? throw new NotFoundException($"Код «{raw}» не найден: это не место хранения, не артикул и не штрих-код позиции.");
    }

    private async Task<ScanResultDto?> FindLocationAsync(string code, CancellationToken ct)
    {
        var normalized = LocationCodeMatch.Normalize(code);
        if (normalized is null)
            return null;

        var location = await locations.GetLocationByFullCodeAsync(normalized, ct);
        return location is null
            ? null
            : new ScanResultDto(ScanKind.Location, location.FullCode, location.Id, location.FullCode, location.Name);
    }

    private async Task<ScanResultDto?> FindItemAsync(string code, CancellationToken ct)
    {
        var item = await catalog.FindItemByCodeAsync(code, ct);
        return item is null
            ? null
            : new ScanResultDto(ScanKind.Item, item.Sku, item.Id, item.Name, item.Sku);
    }
}

/// <summary>Ссылки, которые зашиваются в QR-коды наклеек: их открывает обычная камера телефона.</summary>
public static class LabelLinks
{
    public const string LocationPath = "/l/";
    public const string ItemPath = "/i/";

    public static string ForLocation(string? baseUrl, string fullCode) => Build(baseUrl, LocationPath, fullCode);

    public static string ForItem(string? baseUrl, string sku) => Build(baseUrl, ItemPath, sku);

    /// <summary>Без адреса приложения в QR — только сам код (его распознаёт сканер внутри приложения).</summary>
    private static string Build(string? baseUrl, string path, string code) =>
        string.IsNullOrWhiteSpace(baseUrl) ? code : $"{baseUrl.Trim().TrimEnd('/')}{path}{Uri.EscapeDataString(code)}";

    /// <summary>Разбирает ссылку с наклейки (адрес сервера не важен — мог смениться).</summary>
    public static bool TryParse(string text, out ScanKind kind, out string code)
    {
        kind = default;
        code = "";
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return false;

        var path = uri.AbsolutePath;
        foreach (var (prefix, k) in new[] { (LocationPath, ScanKind.Location), (ItemPath, ScanKind.Item) })
        {
            var at = path.LastIndexOf(prefix, StringComparison.Ordinal);
            if (at < 0)
                continue;
            var value = Uri.UnescapeDataString(path[(at + prefix.Length)..]).Trim();
            if (value.Length == 0 || value.Contains('/'))
                continue;
            kind = k;
            code = value;
            return true;
        }
        return false;
    }
}
