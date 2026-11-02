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

        var found = await TryResolveAsync(raw, ct);

        // Ручной сканер при русской раскладке печатает «не те» буквы — пробуем как в английской.
        if (found is null && KeyboardLayout.HasRussianLetters(raw))
            found = await TryResolveAsync(KeyboardLayout.FromRussianLayout(raw), ct);

        if (found is null && (LabelCodes.TryParse(raw, out var shortKind, out _)
                              || LabelCodes.TryParse(KeyboardLayout.FromRussianLayout(raw), out shortKind, out _)))
            throw new NotFoundException(shortKind == ScanKind.Location
                ? "Место хранения с этой наклейки не найдено (удалено?)."
                : "Позиция с этой наклейки не найдена (удалена?).");

        return found ?? throw new NotFoundException(LabelLinks.TryParse(raw, out var kind, out var value)
            ? kind == ScanKind.Location
                ? $"Место хранения «{value}» не найдено (наклейка от удалённого или переименованного места?)."
                : $"Позиция «{value}» не найдена."
            : $"Код «{raw}» не найден: это не место хранения, не артикул и не штрих-код позиции.");
    }

    private async Task<ScanResultDto?> TryResolveAsync(string raw, CancellationToken ct)
    {
        // Короткий код с нашей наклейки: SWL-15 (место хранения) или SWI-7 (позиция).
        if (LabelCodes.TryParse(raw, out var codeKind, out var id))
            return codeKind == ScanKind.Location ? await FindLocationByIdAsync(id, ct) : await FindItemByIdAsync(id, ct);

        // Ссылка с нашей наклейки: …/l/<код места> или …/i/<артикул>.
        if (LabelLinks.TryParse(raw, out var kind, out var value))
            return kind == ScanKind.Location ? await FindLocationAsync(value, ct) : await FindItemAsync(value, ct);

        return await FindLocationAsync(raw, ct) ?? await FindItemAsync(raw, ct);
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

    private async Task<ScanResultDto?> FindLocationByIdAsync(int id, CancellationToken ct)
    {
        var location = (await locations.GetLocationsAsync([id], ct)).FirstOrDefault();
        return location is null
            ? null
            : new ScanResultDto(ScanKind.Location, location.FullCode, location.Id, location.FullCode, location.Name);
    }

    private async Task<ScanResultDto?> FindItemByIdAsync(int id, CancellationToken ct)
    {
        var item = await catalog.GetItemAsync(id, ct);
        return item is null
            ? null
            : new ScanResultDto(ScanKind.Item, item.Sku, item.Id, item.Name, item.Sku);
    }

    private async Task<ScanResultDto?> FindItemAsync(string code, CancellationToken ct)
    {
        var item = await catalog.FindItemByCodeAsync(code, ct);
        return item is null
            ? null
            : new ScanResultDto(ScanKind.Item, item.Sku, item.Id, item.Name, item.Sku);
    }
}

/// <summary>
/// Короткие коды для QR-наклеек без ссылки: SWL-15 — место хранения № 15, SWI-7 — позиция № 7.
/// Только латиница и цифры (ручной сканер «печатает» их при любой раскладке, а русскую мы распознаём),
/// и код не меняется, если место хранения переименовали.
/// </summary>
public static class LabelCodes
{
    public const string LocationPrefix = "SWL-";
    public const string ItemPrefix = "SWI-";

    public static string ForLocation(int id) => LocationPrefix + id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static string ForItem(int id) => ItemPrefix + id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static bool TryParse(string text, out ScanKind kind, out int id)
    {
        kind = default;
        id = 0;
        var value = text.Trim();
        foreach (var (prefix, k) in new[] { (LocationPrefix, ScanKind.Location), (ItemPrefix, ScanKind.Item) })
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(value.AsSpan(prefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out id)
                && id > 0)
            {
                kind = k;
                return true;
            }
        }
        id = 0;
        return false;
    }
}

/// <summary>Ссылки, которые зашиваются в QR-коды наклеек: их открывает обычная камера телефона.</summary>
public static class LabelLinks
{
    public const string LocationPath = "/l/";
    public const string ItemPath = "/i/";

    public static string ForLocation(string? baseUrl, string fullCode) => Build(baseUrl, LocationPath, fullCode);

    public static string ForItem(string? baseUrl, string sku) => Build(baseUrl, ItemPath, sku);

    /// <summary>
    /// Без адреса приложения — только путь «/l/%D0%A1%D0%9A1-A-01»: его распознаёт сканер в приложении.
    /// Кириллица кодируется, поэтому в QR только латиница — её «напечатает» любой ручной сканер.
    /// </summary>
    private static string Build(string? baseUrl, string path, string code) =>
        $"{(string.IsNullOrWhiteSpace(baseUrl) ? "" : baseUrl.Trim().TrimEnd('/'))}{path}{Uri.EscapeDataString(code)}";

    /// <summary>Разбирает ссылку с наклейки (адрес сервера не важен — мог смениться).</summary>
    public static bool TryParse(string text, out ScanKind kind, out string code)
    {
        kind = default;
        code = "";

        string path;
        if (text.StartsWith('/'))
            path = text.Split('?', '#')[0]; // путь без адреса сервера
        else if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            path = uri.AbsolutePath;
        else
            return false;

        foreach (var (prefix, k) in new[] { (LocationPath, ScanKind.Location), (ItemPath, ScanKind.Item) })
        {
            // Сканер с включённым Caps Lock может прислать «/L/» — регистр пути не важен.
            var at = path.LastIndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                continue;
            string value;
            try
            {
                value = Uri.UnescapeDataString(path[(at + prefix.Length)..]).Trim();
            }
            catch (UriFormatException)
            {
                continue;
            }
            if (value.Length == 0 || value.Contains('/'))
                continue;
            kind = k;
            code = value;
            return true;
        }
        return false;
    }
}
