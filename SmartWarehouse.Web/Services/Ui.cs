using System.Globalization;
using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.Web.Services;

/// <summary>Подписи и форматирование для интерфейса.</summary>
public static class Ui
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public static string DocType(MovementType type) => type switch
    {
        MovementType.Receipt => "Приход",
        MovementType.Issue => "Выдача",
        MovementType.Transfer => "Перемещение",
        MovementType.WriteOff => "Списание",
        MovementType.Adjustment => "Корректировка",
        MovementType.Return => "Возврат",
        _ => type.ToString()
    };

    public static string DocTypeClass(MovementType type) => type switch
    {
        MovementType.Receipt or MovementType.Return => "tag-in",
        MovementType.Issue or MovementType.WriteOff => "tag-out",
        _ => "tag-neutral"
    };

    public static string LocType(LocationType type) => type switch
    {
        LocationType.Zone => "Зона",
        LocationType.Rack => "Стеллаж",
        LocationType.Shelf => "Полка",
        LocationType.Cell => "Ячейка",
        _ => type.ToString()
    };

    /// <summary>Как подписать подотчётное лицо в документе этого типа.</summary>
    public static string AccountableLabel(MovementType type) => type switch
    {
        MovementType.Issue => "Кому выдано",
        MovementType.WriteOff => "С кого списано",
        MovementType.Return => "Кто вернул",
        _ => "Сотрудник"
    };

    /// <summary>Количество без лишних нулей: 12, 4,5, 0,125.</summary>
    public static string Qty(decimal value) => value.ToString("#,0.###", Ru);

    public static string Date(DateTimeOffset value) => value.ToLocalTime().ToString("dd.MM.yyyy HH:mm", Ru);

    /// <summary>Адрес фото позиции (через веб-приложение); версия сбрасывает кеш браузера после замены.</summary>
    public static string? ImageUrl(int itemId, long? version) =>
        version is null ? null : $"/item-image/{itemId}?v={version}";
}
