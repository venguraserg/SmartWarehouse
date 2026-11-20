namespace SmartWarehouse.Web.Services;

/// <summary>Варианты оформления интерфейса (палитры — в app.css: html[data-theme=…]).</summary>
public static class UiThemes
{
    /// <summary>Cookie с оформлением текущего пользователя: страница сразу рисуется в нужных цветах.</summary>
    public const string CookieName = "sw.theme";

    /// <summary>По умолчанию — «Хвойный»: ближе всего к фирменному стилю.</summary>
    public const string Default = "pine";

    public sealed record Theme(string Key, string Name, string Description, string Side, string SideActive, string Bg, string Primary, string Border, string Surface);

    public static readonly IReadOnlyList<Theme> All =
    [
        new("cream", "Сливочный", "Светлый и тёплый: фон с оттенком сливок, коричневый текст, зелёные кнопки.",
            "#F6EFDF", "#FFFFFF", "#FBF8F1", "#004820", "#E9E0CF", "#FFFFFF"),
        new("pine", "Хвойный", "Меню фирменного тёмно-зелёного цвета, светлая рабочая область. Ближе всего к фирменному стилю.",
            "#004820", "#0B5B30", "#F3F5F2", "#004820", "#DDE3DE", "#FFFFFF"),
        new("choco", "Шоколадный", "Меню шоколадного цвета, тёплый светлый фон, зелёные действия. Мягкий контраст — «молочная» тема.",
            "#401F0D", "#56311D", "#F8F5F0", "#004820", "#E7DED3", "#FFFFFF"),
        new("graphite", "Графитовый", "Почти нейтральная гамма: графитовое меню, приглушённый зелёный. Самый спокойный для долгой работы.",
            "#1F2523", "#2E3733", "#F2F3F2", "#2E5B43", "#DCE0DE", "#FFFFFF"),
        new("sage", "Шалфей", "Полностью светлый: белое меню, шалфейные оттенки фирменного зелёного. Самый «воздушный».",
            "#FFFFFF", "#EAF0E6", "#F4F6F1", "#3F6B4B", "#DFE5DB", "#FFFFFF"),
    ];

    /// <summary>Известный ключ — как есть, иначе оформление по умолчанию.</summary>
    public static string Normalize(string? key) =>
        key is not null && All.Any(t => t.Key == key) ? key : Default;
}
