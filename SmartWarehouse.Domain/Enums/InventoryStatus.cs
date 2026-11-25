namespace SmartWarehouse.Domain.Enums;

/// <summary>Состояние инвентаризации.</summary>
public enum InventoryStatus
{
    /// <summary>Идёт подсчёт.</summary>
    Open = 0,

    /// <summary>Проведена: расхождения оформлены документами, места отмечены проверенными.</summary>
    Posted = 1,

    /// <summary>Отменена без изменений остатков.</summary>
    Cancelled = 2
}
