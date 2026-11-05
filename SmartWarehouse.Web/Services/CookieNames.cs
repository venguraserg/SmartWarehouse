using System.Text.RegularExpressions;

namespace SmartWarehouse.Web.Services;

/// <summary>Имена cookie для нескольких копий приложения на одном сервере (на разных портах).</summary>
public static partial class CookieNames
{
    [GeneratedRegex(@":(\d+)/?$")]
    private static partial Regex PortPattern();

    /// <summary>
    /// «http://0.0.0.0:8200» → «.8200»; порт 80, 443 или не задан → "" (основная установка, имена как раньше).
    /// Если адресов несколько («http://+:8200;https://+:8201»), берётся первый.
    /// </summary>
    public static string PortSuffix(string? urls)
    {
        var first = urls?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (first is null)
            return "";
        var match = PortPattern().Match(first);
        return match.Success && match.Groups[1].Value is not ("80" or "443") ? "." + match.Groups[1].Value : "";
    }
}
