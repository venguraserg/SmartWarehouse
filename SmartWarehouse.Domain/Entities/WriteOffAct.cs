using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.Domain.Entities;

/// <summary>
/// Акт на списание: сотрудник отчитывается, куда израсходовал полученное под отчёт.
/// Может быть на одну работу или сводным за период. Порядок:
/// черновик → на утверждении (позиции зарезервированы) → утверждён (проведено списание)
/// или возвращён на доработку с замечанием.
/// Утверждает непосредственный руководитель сотрудника; у кого руководителя нет — утверждает сам.
/// </summary>
public class WriteOffAct
{
    public int Id { get; set; }

    /// <summary>Номер акта: АС-000012.</summary>
    public string Number => FormatNumber(Id);

    public ActStatus Status { get; set; } = ActStatus.Draft;

    /// <summary>Подотчётное лицо, с которого списывается.</summary>
    public int EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    /// <summary>Период сводного акта (необязательно): с какого и по какое число израсходовано.</summary>
    public DateOnly? PeriodFrom { get; set; }

    public DateOnly? PeriodTo { get; set; }

    /// <summary>Наряд, заявка — основание.</summary>
    public string? ExternalNumber { get; set; }

    public string? Comment { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Кто создал (текстом): сам сотрудник или кладовщик за него.</summary>
    public string? CreatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    /// <summary>Кто утвердил или вернул — сотрудник и текстом на тот момент.</summary>
    public int? DecidedById { get; set; }

    public Employee? DecidedByEmployee { get; set; }

    public string? DecidedBy { get; set; }

    public string? DecidedByPosition { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>Замечание руководителя при возврате на доработку.</summary>
    public string? ReturnRemark { get; set; }

    /// <summary>Документ списания, проведённый при утверждении.</summary>
    public long? DocumentId { get; set; }

    public StockDocument? Document { get; set; }

    public ICollection<WriteOffActLine> Lines { get; set; } = new List<WriteOffActLine>();

    /// <summary>Акт можно менять и удалять: черновик или возвращённый.</summary>
    public bool IsEditable => Status is ActStatus.Draft or ActStatus.Returned;

    public static string FormatNumber(int id) => $"АС-{id:D6}";
}

/// <summary>Строка акта: что, сколько и куда использовано.</summary>
public class WriteOffActLine
{
    public int Id { get; set; }

    public int ActId { get; set; }

    public WriteOffAct Act { get; set; } = null!;

    public int LineNo { get; set; }

    public int ItemId { get; set; }

    public Item Item { get; set; } = null!;

    public decimal Quantity { get; set; }

    /// <summary>Оборудование, на которое установлено / по которому израсходовано.</summary>
    public int? EquipmentId { get; set; }

    public Equipment? Equipment { get; set; }

    /// <summary>Куда использовано — свободным текстом («замена подшипника, наряд 15»).</summary>
    public string? Usage { get; set; }

    /// <summary>Когда израсходовано (для сводного акта).</summary>
    public DateOnly? UsedOn { get; set; }
}
