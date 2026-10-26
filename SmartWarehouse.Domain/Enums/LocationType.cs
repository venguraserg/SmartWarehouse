namespace SmartWarehouse.Domain.Enums;

/// <summary>
/// Уровень места хранения. Вложенность идёт только «вниз»:
/// зона → стеллаж → полка → ячейка (любые уровни можно пропускать).
/// </summary>
public enum LocationType
{
    /// <summary>Зона или помещение склада.</summary>
    Zone = 1,

    /// <summary>Стеллаж.</summary>
    Rack = 2,

    /// <summary>Полка.</summary>
    Shelf = 3,

    /// <summary>Ячейка.</summary>
    Cell = 4
}
