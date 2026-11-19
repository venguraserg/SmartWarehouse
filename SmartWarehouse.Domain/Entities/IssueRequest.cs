using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.Domain.Entities;

/// <summary>
/// Запрос на выдачу: сотрудник выбирает из остатков, что ему нужно, — кладовщик (или старший смены)
/// собирает заказ и оформляет выдачу с уже заполненными строками. Остатки запрос не резервирует.
/// Порядок: открыт → выдан (документ выдачи) / отменён сотрудником / отклонён кладовщиком.
/// </summary>
public class IssueRequest
{
    public int Id { get; set; }

    /// <summary>Номер ЗВ-000012.</summary>
    public string Number => FormatNumber(Id);

    public IssueRequestStatus Status { get; set; } = IssueRequestStatus.Open;

    /// <summary>Кому выдать.</summary>
    public int EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    /// <summary>Для чего, срочность — свободным текстом.</summary>
    public string? Comment { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Кто создал (текстом): сам сотрудник или кладовщик с его слов.</summary>
    public string? CreatedBy { get; set; }

    /// <summary>Когда выдан, отменён или отклонён.</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>Кто выдал, отменил или отклонил (текстом).</summary>
    public string? ClosedBy { get; set; }

    /// <summary>Причина отклонения.</summary>
    public string? CloseNote { get; set; }

    /// <summary>Документ выдачи по запросу.</summary>
    public long? DocumentId { get; set; }

    public StockDocument? Document { get; set; }

    public ICollection<IssueRequestLine> Lines { get; set; } = new List<IssueRequestLine>();

    public static string FormatNumber(int id) => $"ЗВ-{id:D6}";
}

/// <summary>Строка запроса: позиция и сколько нужно.</summary>
public class IssueRequestLine
{
    public int Id { get; set; }

    public int RequestId { get; set; }

    public IssueRequest Request { get; set; } = null!;

    public int LineNo { get; set; }

    public int ItemId { get; set; }

    public Item Item { get; set; } = null!;

    public decimal Quantity { get; set; }
}
