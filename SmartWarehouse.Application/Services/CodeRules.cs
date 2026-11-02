using System.Text.RegularExpressions;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Application.Services;

/// <summary>
/// Правила для кодов склада, мест хранения, артикулов и штрих-кодов: они попадают в QR-коды и вводятся сканером,
/// поэтому — только латиница, цифры и немного знаков (ручной сканер «печатает» их при любой раскладке).
/// </summary>
internal static partial class CodeRules
{
    public const char Separator = '-';
    public const int MaxPartLength = 16;

    public const int MaxSkuLength = 64;
    public const int MaxSkuPrefixLength = 6;

    [GeneratedRegex(@"^[A-Z0-9]+$")]
    private static partial Regex PartPattern();

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9._/-]*$")]
    private static partial Regex SkuPattern();

    [GeneratedRegex(@"^[A-Z]+$")]
    private static partial Regex SkuPrefixPattern();

    [GeneratedRegex(@"^[\x21-\x7E]+$")]
    private static partial Regex BarcodePattern();

    /// <summary>Проверяет часть кода (только буквы и цифры) и приводит её к верхнему регистру.</summary>
    public static string NormalizePart(string? value, string what)
    {
        var code = value?.Trim().ToUpperInvariant();

        if (string.IsNullOrEmpty(code))
            throw new DomainException($"{what}: код обязателен.");
        if (code.Length > MaxPartLength)
            throw new DomainException($"{what}: код длиннее {MaxPartLength} символов.");
        if (!PartPattern().IsMatch(code))
            throw new DomainException($"{what}: код может содержать только латинские буквы и цифры (без пробелов и «{Separator}»).");

        return code;
    }

    /// <summary>Артикул: латинские буквы, цифры и «- . _ /», верхний регистр, без пробелов.</summary>
    public static string NormalizeSku(string? value)
    {
        var sku = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(sku))
            throw new DomainException("Артикул обязателен.");
        if (sku.Length > MaxSkuLength)
            throw new DomainException($"Артикул — не длиннее {MaxSkuLength} символов.");
        if (!SkuPattern().IsMatch(sku))
            throw new DomainException("Артикул: только латинские буквы, цифры и знаки «- . _ /», без пробелов. Например, SP-000012.");
        return sku;
    }

    /// <summary>Префикс автоартикула категории (английское сокращение: BRG, BLT…); пусто — общий SP.</summary>
    public static string? NormalizeSkuPrefix(string? value)
    {
        var prefix = value?.Trim().TrimEnd('-').ToUpperInvariant();
        if (string.IsNullOrEmpty(prefix))
            return null;
        if (prefix.Length > MaxSkuPrefixLength || !SkuPrefixPattern().IsMatch(prefix))
            throw new DomainException($"Префикс артикула — от 1 до {MaxSkuPrefixLength} латинских букв, например BRG.");
        return prefix;
    }

    /// <summary>Штрих-код: латиница, цифры и знаки, без пробелов и кириллицы.</summary>
    public static bool IsValidBarcode(string barcode) => BarcodePattern().IsMatch(barcode);
}
