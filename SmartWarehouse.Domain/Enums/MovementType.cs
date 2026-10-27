namespace SmartWarehouse.Domain.Enums;

/// <summary>Тип складской операции.</summary>
public enum MovementType
{
    /// <summary>Приход на склад (по накладной, из закупки).</summary>
    Receipt,

    /// <summary>Выдача со склада сотруднику: товар уходит из ячейки и числится за сотрудником (подотчёт).</summary>
    Issue,

    /// <summary>Перемещение между местами хранения.</summary>
    Transfer,

    /// <summary>Списание с сотрудника (израсходовано на ремонт, брак, утрата). Оформляется актом.</summary>
    WriteOff,

    /// <summary>Корректировка по результатам инвентаризации.</summary>
    Adjustment,

    /// <summary>Возврат от сотрудника на склад (неиспользованное).</summary>
    Return
}
