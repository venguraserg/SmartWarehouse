using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.Domain.Entities;

/// <summary>
/// Складской документ (шапка): приходная накладная, требование на выдачу, акт списания и т.д.
/// Строки документа — записи журнала <see cref="StockMovement"/>. Документ проводится сразу
/// при создании, одной транзакцией: либо все строки, либо ни одной.
/// </summary>
public class StockDocument
{
    public long Id { get; set; }

    public MovementType Type { get; set; }

    /// <summary>Внутренний номер документа: ПР-000015, ВЫД-000102...</summary>
    public string Number => FormatNumber(Type, Id);

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Номер внешнего документа-основания: накладной поставщика, заявки, акта.</summary>
    public string? ExternalNumber { get; set; }

    /// <summary>Оборудование, на которое выдано или по которому списано.</summary>
    public int? EquipmentId { get; set; }

    public Equipment? Equipment { get; set; }

    /// <summary>Кто провёл документ — сотрудник из справочника.</summary>
    public int? PerformedById { get; set; }

    public Employee? PerformedByEmployee { get; set; }

    /// <summary>
    /// Кто провёл — текстом, как было на момент проведения («Иванов И. П.»).
    /// Сохраняется, чтобы история не менялась при правке справочника.
    /// </summary>
    public string? PerformedBy { get; set; }

    /// <summary>
    /// Подотчётное лицо: кому выдано (выдача), с кого списано (списание), кто вернул (возврат).
    /// </summary>
    public int? ReceivedById { get; set; }

    public Employee? ReceivedByEmployee { get; set; }

    /// <summary>Подотчётное лицо — текстом на момент проведения.</summary>
    public string? ReceivedBy { get; set; }

    public string? Comment { get; set; }

    public ICollection<StockMovement> Lines { get; set; } = new List<StockMovement>();

    public static string FormatNumber(MovementType type, long id)
    {
        var prefix = type switch
        {
            MovementType.Receipt => "ПР",
            MovementType.Issue => "ВЫД",
            MovementType.Transfer => "ПЕР",
            MovementType.WriteOff => "СП",
            MovementType.Adjustment => "КОР",
            MovementType.Return => "ВЗ",
            _ => "ДОК"
        };
        return $"{prefix}-{id:D6}";
    }
}
