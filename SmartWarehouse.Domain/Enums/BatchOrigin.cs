namespace SmartWarehouse.Domain.Enums;

/// <summary>Происхождение партии.</summary>
public enum BatchOrigin
{
    /// <summary>Введена вручную (приход, корректировка).</summary>
    Manual = 0,

    /// <summary>Из накладной на внутреннее перемещение (файл 1С).</summary>
    Invoice = 1,

    /// <summary>Ввод начальных остатков из 1С.</summary>
    Opening = 2
}
