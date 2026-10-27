namespace SmartWarehouse.Application.Services;

/// <summary>
/// Сопоставление кода места с запросом по целым частям кода (между «-»):
/// «СК1-A» — само место и всё внутри; «A-01» без кода склада — тоже подходит.
/// «A-1» при этом не совпадёт с «A-10».
/// </summary>
public static class LocationCodeMatch
{
    public const char Separator = '-';

    /// <summary>Приводит запрос к виду кода: верхний регистр, без пробелов по краям и лишних «-».</summary>
    public static string? Normalize(string? query)
    {
        var code = query?.Trim().Trim(Separator).ToUpperInvariant();
        return string.IsNullOrEmpty(code) ? null : code;
    }

    public static bool Matches(string fullCode, string code) =>
        fullCode == code
        || fullCode.StartsWith(code + Separator)
        || fullCode.EndsWith(Separator + code)
        || fullCode.Contains(Separator + code + Separator);
}
