namespace SmartWarehouse.Application.Services;

/// <summary>
/// Ручной сканер работает как клавиатура: при включённой русской раскладке вместо «http://…/l/…»
/// он «напечатает» «реезЖ..…». Здесь такой текст переводится обратно — как если бы была английская раскладка.
/// </summary>
public static class KeyboardLayout
{
    // Символ русской раскладки → символ той же клавиши в английской (ЙЦУКЕН → QWERTY).
    private static readonly Dictionary<char, char> RuToEn = Build();

    private static Dictionary<char, char> Build()
    {
        const string ru = "йцукенгшщзхъфывапролджэячсмитьбюё" + "ЙЦУКЕНГШЩЗХЪФЫВАПРОЛДЖЭЯЧСМИТЬБЮЁ" + ".,\"№;:?";
        const string en = "qwertyuiop[]asdfghjkl;'zxcvbnm,.`" + "QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>~" + "/?@#$^&";
        var map = new Dictionary<char, char>();
        for (var i = 0; i < ru.Length; i++)
            map[ru[i]] = en[i];
        return map;
    }

    /// <summary>Есть ли в тексте буквы русской раскладки (тогда имеет смысл попробовать перевод).</summary>
    public static bool HasRussianLetters(string text) => text.Any(c => c is >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё');

    /// <summary>Текст, набранный в русской раскладке, — как он выглядел бы в английской.</summary>
    public static string FromRussianLayout(string text) =>
        string.Create(text.Length, text, (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
                span[i] = RuToEn.TryGetValue(source[i], out var en) ? en : source[i];
        });
}
