using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.Domain.Entities;

/// <summary>
/// Запись неизменяемого журнала движений. Количество всегда положительное,
/// направление определяют поля <see cref="FromLocationId"/> (откуда убыло) и <see cref="ToLocationId"/> (куда прибыло).
/// </summary>
public class StockMovement
{
    public long Id { get; set; }

    public MovementType Type { get; set; }

    public int ItemId { get; set; }

    public Item Item { get; set; } = null!;

    /// <summary>Откуда убыло (выдача, списание, перемещение, отрицательная корректировка).</summary>
    public int? FromLocationId { get; set; }

    public StorageLocation? FromLocation { get; set; }

    /// <summary>Куда прибыло (приход, перемещение, положительная корректировка).</summary>
    public int? ToLocationId { get; set; }

    public StorageLocation? ToLocation { get; set; }

    public decimal Quantity { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Кто выполнил операцию (до появления авторизации указывается в запросе).</summary>
    public string? PerformedBy { get; set; }

    /// <summary>Основание: номер накладной, заявки, акта.</summary>
    public string? Document { get; set; }

    public string? Comment { get; set; }

    /// <summary>Оборудование, на которое выдана запчасть.</summary>
    public int? EquipmentId { get; set; }

    public Equipment? Equipment { get; set; }
}
