using SmartWarehouse.Domain.Enums;

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

    /// <summary>
    /// Вне бухучёта: в 1С не числится (бывшее в употреблении, снятое с оборудования, неучтённое).
    /// Цена необязательна; в сверке с 1С не участвует.
    /// </summary>
    public bool OffBook { get; set; }

    /// <summary>Как партия попала в приложение.</summary>
    public BatchOrigin Origin { get; set; }
}

/// <summary>
/// Расхождение при приёмке: по накладной числится одно количество, фактически привезли другое.
/// Остаток не меняется (в учёте главная 1С) — запись для бухгалтерии, пока не разберутся.
/// </summary>
public class ReceiptDiscrepancy
{
    public int Id { get; set; }

    public int BatchId { get; set; }

    public Batch Batch { get; set; } = null!;

    /// <summary>По накладной.</summary>
    public decimal Expected { get; set; }

    /// <summary>Фактически.</summary>
    public decimal Actual { get; set; }

    public string? Comment { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string? CreatedBy { get; set; }

    /// <summary>Когда закрыто (бухгалтерия исправила документ, досдали товар и т.п.).</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    public string? Resolution { get; set; }
}
