namespace SmartWarehouse.Domain.Enums;

/// <summary>Состояние запроса на выдачу.</summary>
public enum IssueRequestStatus
{
    /// <summary>Ждёт кладовщика (или старшего смены).</summary>
    Open = 0,

    /// <summary>Выдано: проведён документ выдачи (возможно, не всё — что было на складе).</summary>
    Issued = 1,

    /// <summary>Сотрудник отменил запрос сам.</summary>
    Cancelled = 2,

    /// <summary>Кладовщик отклонил запрос с причиной.</summary>
    Rejected = 3
}
