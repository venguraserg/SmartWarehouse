namespace SmartWarehouse.Domain.Entities;

/// <summary>
/// Сколько позиции числится за сотрудником (подотчёт): выдано, но ещё не списано и не возвращено.
/// Как и остаток на складе, меняется только проведением документов.
/// </summary>
public class EmployeeBalance
{
    public int EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    public int ItemId { get; set; }

    public Item Item { get; set; } = null!;

    public decimal Quantity { get; set; }
}
