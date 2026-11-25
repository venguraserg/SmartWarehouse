namespace SmartWarehouse.Application.Services;

/// <summary>
/// Поиск по словам: «подшипник 6205» находит «Подшипник шариковый 6205-2RS» — каждое слово ищется отдельно,
/// в любом порядке, без учёта регистра. Позиция подходит, если нашлись все слова (в названии, артикуле или заметке).
/// </summary>
public static class SearchWords
{
    /// <summary>Больше слов не берём — длинная фраза почти наверняка ничего не найдёт и тяжела для базы.</summary>
    public const int MaxWords = 8;

    /// <summary>Символ экранирования для LIKE: «%» и «_» в запросе ищутся как обычные символы.</summary>
    public const string LikeEscape = "\\";

    private static readonly char[] Separators = [' ', '\t', '\r', '\n', ',', ';'];

    /// <summary>Слова запроса без повторов (дефис и точка слова не делят: «6205-2RS», «1.C3» — одно слово).</summary>
    public static IReadOnlyList<string> Split(string? query) =>
        string.IsNullOrWhiteSpace(query)
            ? []
            : query.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxWords)
                .ToList();

    /// <summary>Шаблон «содержит слово» для ILIKE, с экранированием «%», «_» и «\».</summary>
    public static string LikePattern(string word) =>
        "%" + word.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    /// <summary>Все слова запроса есть хотя бы в одном из полей (пустой запрос подходит ко всему).</summary>
    public static bool Matches(string? query, params string?[] fields) => MatchesAll(Split(query), fields);

    public static bool MatchesAll(IReadOnlyList<string> words, params string?[] fields) =>
        words.All(w => fields.Any(f => f?.Contains(w, StringComparison.OrdinalIgnoreCase) == true));

    /// <summary>Первое по тексту вхождение любого из слов: позиция и само слово (-1, если ни одного).</summary>
    public static (int Index, string Word) FirstMatch(string text, IReadOnlyList<string> words)
    {
        var best = (Index: -1, Word: string.Empty);
        foreach (var w in words)
        {
            var at = text.IndexOf(w, StringComparison.OrdinalIgnoreCase);
            if (at >= 0 && (best.Index < 0 || at < best.Index))
                best = (at, w);
        }
        return best;
    }
}
