namespace SmartWarehouse.Domain.Enums;

/// <summary>Состояние акта на списание.</summary>
public enum ActStatus
{
    /// <summary>Черновик: сотрудник заполняет, ничего не зарезервировано.</summary>
    Draft,

    /// <summary>На утверждении у руководителя: позиции зарезервированы (их нельзя вернуть или включить в другой акт).</summary>
    Pending,

    /// <summary>Утверждён: позиции списаны с сотрудника документом списания.</summary>
    Approved,

    /// <summary>Возвращён руководителем на доработку с замечанием; резерв снят.</summary>
    Returned
}
