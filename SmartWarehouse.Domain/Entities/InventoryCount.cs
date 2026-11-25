using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.Domain.Entities;

/// <summary>
/// Инвентаризация места (обычно стеллажа) со всем вложенным: кладовщик сканирует ячейки и вводит факт,
/// затем проводит — расхождения оформляются корректировками и перемещениями.
/// </summary>
public class InventoryCount
{
    public int Id { get; set; }

    /// <summary>«ИНВ-3».</summary>
    public string Number => $"ИНВ-{Id}";

    /// <summary>Что проверяем: стеллаж (или любое место) со всем вложенным.</summary>
    public int RootLocationId { get; set; }

    public StorageLocation RootLocation { get; set; } = null!;

    public InventoryStatus Status { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public int? StartedById { get; set; }

    public string StartedBy { get; set; } = string.Empty;

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Кто провёл или отменил.</summary>
    public string? FinishedBy { get; set; }

    public string? Comment { get; set; }

    public List<InventoryLine> Lines { get; set; } = [];

    public List<InventoryPlace> Places { get; set; } = [];
}

/// <summary>Место, отмеченное «проверено» в этой инвентаризации (все позиции посчитаны).</summary>
public class InventoryPlace
{
    public int Id { get; set; }

    public int InventoryId { get; set; }

    public InventoryCount Inventory { get; set; } = null!;

    public int LocationId { get; set; }

    public StorageLocation Location { get; set; } = null!;

    public DateTimeOffset CheckedAt { get; set; }

    public string CheckedBy { get; set; } = string.Empty;
}

/// <summary>
/// Подсчитанная позиция в месте. Expected — сколько числилось в месте в момент подсчёта:
/// при проведении в документ идёт разница «факт − числилось», и выдачи после подсчёта её не искажают.
/// </summary>
public class InventoryLine
{
    public int Id { get; set; }

    public int InventoryId { get; set; }

    public InventoryCount Inventory { get; set; } = null!;

    public int LocationId { get; set; }

    public StorageLocation Location { get; set; } = null!;

    public int ItemId { get; set; }

    public Item Item { get; set; } = null!;

    public decimal Expected { get; set; }

    public decimal Counted { get; set; }

    public DateTimeOffset CountedAt { get; set; }

    public string CountedBy { get; set; } = string.Empty;
}
