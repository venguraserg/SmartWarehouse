using System.Text.RegularExpressions;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Application.Services;

/// <summary>Правила для кодов склада и мест хранения: из них собирается содержимое QR-метки.</summary>
internal static partial class CodeRules
{
    public const char Separator = '-';
    public const int MaxPartLength = 16;

    [GeneratedRegex(@"^[\p{L}\d]+$")]
    private static partial Regex PartPattern();

    /// <summary>Проверяет часть кода (только буквы и цифры) и приводит её к верхнему регистру.</summary>
    public static string NormalizePart(string? value, string what)
    {
        var code = value?.Trim().ToUpperInvariant();

        if (string.IsNullOrEmpty(code))
            throw new DomainException($"{what}: код обязателен.");
        if (code.Length > MaxPartLength)
            throw new DomainException($"{what}: код длиннее {MaxPartLength} символов.");
        if (!PartPattern().IsMatch(code))
            throw new DomainException($"{what}: код может содержать только буквы и цифры (без пробелов и «{Separator}»).");

        return code;
    }
}
