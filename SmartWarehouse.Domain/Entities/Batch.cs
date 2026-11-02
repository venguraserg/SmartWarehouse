namespace SmartWarehouse.Domain.Entities;

/// <summary>
/// Партия: количество одной позиции, поступившее одним приходом по одной цене.
/// Одна и та же позиция может лежать партиями с разными ценами, в разных местах.
/// Партия «идёт» за товаром: ячейка → сотрудник (подотчёт) → акт на списание —
/// поэтому списывается по цене именно того, что взяли.
/// </summary>
public class Batch
{
    public int Id { get; set; }

    public int ItemId { get; set; }

    public Item Item { get; set; } = null!;

    /// <summary>Цена за единицу. null — цена не указана (остатки, внесённые до введения цен).</summary>
    public decimal? UnitPrice { get; set; }

    /// <summary>Когда поступила (для выбора «самой старой» партии).</summary>
    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>Документ прихода (или корректировки «+»), которым создана партия.</summary>
    public long? ReceiptDocumentId { get; set; }

    public StockDocument? ReceiptDocument { get; set; }

    public string? Note { get; set; }
}
