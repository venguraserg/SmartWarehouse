using System.Text;
using SmartWarehouse.Application.Services;

namespace SmartWarehouse.Infrastructure.Data;

/// <summary>
/// SQL для миграции LatinCodes: коды складов, мест хранения и артикулы с кириллицей переводятся в латиницу
/// тем же правилом, что <see cref="Transliteration.ToLatin"/> (СК1 → WH1, ЗЧ- → SP-, остальное — по звучанию).
/// Если после перевода два кода совпали бы, таблица не меняется (сообщение в журнале PostgreSQL) — коды правит администратор.
/// Склады и места переводятся вместе: полный код места начинается с кода склада.
/// </summary>
public static class LatinCodesSql
{
    public static string Build()
    {
        var wh = ToLatin("\"Code\"");
        // Собственный код места — не начало полного кода (там код склада), поэтому сокращения к нему не применяются.
        var loc = Letters("\"Code\"");
        var full = ToLatin("\"FullCode\"");
        var sku = ToLatin("\"Sku\"");

        return $"""
            DO $sw$
            BEGIN
                IF EXISTS (SELECT 1 FROM warehouses GROUP BY {wh} HAVING count(*) > 1)
                   OR EXISTS (SELECT 1 FROM storage_locations GROUP BY {full} HAVING count(*) > 1) THEN
                    RAISE NOTICE 'LatinCodes: коды складов и мест после перевода в латиницу совпали бы — оставлены как есть.';
                ELSE
                    UPDATE warehouses SET "Code" = {wh} WHERE "Code" <> {wh};
                    UPDATE storage_locations SET "Code" = {loc}, "FullCode" = {full} WHERE "Code" <> {loc} OR "FullCode" <> {full};
                END IF;

                IF EXISTS (SELECT 1 FROM items GROUP BY {sku} HAVING count(*) > 1) THEN
                    RAISE NOTICE 'LatinCodes: артикулы после перевода в латиницу совпали бы — оставлены как есть.';
                ELSE
                    UPDATE items SET "Sku" = {sku} WHERE "Sku" <> {sku};
                END IF;
            END
            $sw$;
            """;
    }

    /// <summary>Выражение SQL: сокращение в начале (в любом регистре) → английское, затем буквы по звучанию.</summary>
    internal static string ToLatin(string column)
    {
        var sb = new StringBuilder("CASE");
        foreach (var (ru, en) in Transliteration.Prefixes)
        {
            var variants = CaseVariants(ru).Select(v => $"'{v}'");
            sb.Append($" WHEN left({column}, {ru.Length}) IN ({string.Join(", ", variants)}) THEN '{en}' || {Letters($"substr({column}, {ru.Length + 1})")}");
        }
        sb.Append($" ELSE {Letters(column)} END");
        return sb.ToString();
    }

    private static string Letters(string expression)
    {
        var result = expression;
        foreach (var (c, latin) in Transliteration.Letters)
            result = $"replace({result}, '{c}', '{latin}')";
        return result;
    }

    /// <summary>Все сочетания регистров: «ЗЧ-» → «ЗЧ-», «Зч-», «зЧ-», «зч-».</summary>
    private static IEnumerable<string> CaseVariants(string text)
    {
        IEnumerable<string> result = [""];
        foreach (var c in text)
        {
            var options = char.IsLetter(c) ? new[] { char.ToUpperInvariant(c), char.ToLowerInvariant(c) } : new[] { c };
            result = result.SelectMany(prefix => options.Select(o => prefix + o)).ToList();
        }
        return result;
    }
}
