namespace SmartWarehouse.Domain.Enums;

/// <summary>Тип складской операции.</summary>
public enum MovementType
{
    /// <summary>Приход на склад (по накладной, из закупки).</summary>
    Receipt,

    /// <summary>Выдача со склада (на оборудование, участок, ремонт).</summary>
    Issue,

    /// <summary>Перемещение между местами хранения.</summary>
    Transfer,

    /// <summary>Списание (брак, утрата, истёк срок).</summary>
    WriteOff,

    /// <summary>Корректировка по результатам инвентаризации.</summary>
    Adjustment
}
