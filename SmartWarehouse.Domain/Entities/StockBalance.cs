namespace SmartWarehouse.Domain.Entities;

/// <summary>
/// Остаток позиции на месте хранения.
/// Это производная величина: изменяется только вместе с записью в журнале <see cref="StockMovement"/>
/// и никогда не редактируется напрямую.
/// </summary>
public class StockBalance
{
    public int ItemId { get; set; }

    public Item Item { get; set; } = null!;

    public int LocationId { get; set; }

    public StorageLocation Location { get; set; } = null!;

    public decimal Quantity { get; set; }
}
