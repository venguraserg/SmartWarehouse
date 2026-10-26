using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.Domain.Entities;

/// <summary>
/// Строка документа и одновременно запись неизменяемого журнала движений.
/// Количество всегда положительное, направление определяют <see cref="FromLocationId"/>
/// (откуда убыло) и <see cref="ToLocationId"/> (куда прибыло).
/// </summary>
public class StockMovement
{
    public long Id { get; set; }

    public long DocumentId { get; set; }

    public StockDocument Document { get; set; } = null!;

    /// <summary>Номер строки в документе (с 1).</summary>
    public int LineNo { get; set; }

    /// <summary>Копия типа и даты документа — для быстрых выборок истории по позиции.</summary>
    public MovementType Type { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public int ItemId { get; set; }

    public Item Item { get; set; } = null!;

    /// <summary>Откуда убыло (выдача, списание, перемещение, отрицательная корректировка).</summary>
    public int? FromLocationId { get; set; }

    public StorageLocation? FromLocation { get; set; }

    /// <summary>Куда прибыло (приход, перемещение, положительная корректировка).</summary>
    public int? ToLocationId { get; set; }

    public StorageLocation? ToLocation { get; set; }

    public decimal Quantity { get; set; }
}
