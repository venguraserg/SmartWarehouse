using System.Globalization;
using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.Web.Services;

/// <summary>
/// Подготовка кодов складов и мест хранения в форме: те же правила, что проверяет сервер
/// (только буквы и цифры, верхний регистр, до 16 символов), плюс подбор следующего свободного кода.
/// </summary>
public static class Codes
{
    public const int MaxLength = 16;

    /// <summary>Приводит ввод к допустимому коду: верхний регистр, без пробелов, дефисов и прочих знаков.</summary>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var chars = input.ToUpperInvariant().Where(char.IsLetterOrDigit).Take(MaxLength).ToArray();
        return new string(chars);
    }

    /// <summary>
    /// Следующий свободный код среди соседних мест: для зон и стеллажей — буквы A, B, C…,
    /// для полок и ячеек — номера 01, 02, 03… (ширина номера сохраняется по соседям).
    /// </summary>
    public static string NextLocationCode(LocationType type, IEnumerable<string> siblingCodes)
    {
        var used = siblingCodes.Select(Normalize).ToHashSet();

        if (type is LocationType.Zone or LocationType.Rack)
        {
            for (var c = 'A'; c <= 'Z'; c++)
            {
                if (!used.Contains(c.ToString()))
                    return c.ToString();
            }
        }

        return NextNumber(used, minWidth: 2);
    }

    /// <summary>Следующий код склада: СК1, СК2…</summary>
    public static string NextWarehouseCode(IEnumerable<string> existingCodes) =>
        NextWithPrefix("СК", existingCodes, width: 1);

    /// <summary>Следующий код оборудования: внутри родителя — «код родителя-01», на верхнем уровне — «УЧ-01».</summary>
    public static string NextEquipmentCode(string? parentCode, IEnumerable<string> existingCodes) =>
        NextWithPrefix(parentCode is null ? "УЧ-" : $"{parentCode}-", existingCodes, width: 2);

    private static string NextWithPrefix(string prefix, IEnumerable<string> existing, int width)
    {
        var max = existing
            .Where(c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(c => int.TryParse(c.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();

        return prefix + (max + 1).ToString(new string('0', width), CultureInfo.InvariantCulture);
    }

    private static string NextNumber(IReadOnlySet<string> used, int minWidth)
    {
        var numeric = used
            .Where(c => c.Length > 0 && c.All(char.IsAsciiDigit))
            .ToList();

        var width = numeric.Select(c => c.Length).DefaultIfEmpty(minWidth).Max();
        var next = numeric.Select(c => int.Parse(c, CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max() + 1;

        var code = next.ToString(new string('0', Math.Max(width, minWidth)), CultureInfo.InvariantCulture);
        while (used.Contains(code))
            code = (++next).ToString(new string('0', Math.Max(width, minWidth)), CultureInfo.InvariantCulture);

        return code;
    }
}
