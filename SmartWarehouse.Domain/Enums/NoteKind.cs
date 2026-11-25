namespace SmartWarehouse.Domain.Enums;

/// <summary>Вид заметки к позиции номенклатуры.</summary>
public enum NoteKind
{
    /// <summary>Чем можно заменить: аналоги других производителей, допустимые замены.</summary>
    Analog = 1,

    /// <summary>Где стоит: на каком оборудовании или узле применяется.</summary>
    Usage = 2,

    /// <summary>Совет: как ставить, хранить, проверять.</summary>
    Tip = 3,

    /// <summary>Проблема: брак партии, частые отказы, неподходящие замены.</summary>
    Problem = 4
}
