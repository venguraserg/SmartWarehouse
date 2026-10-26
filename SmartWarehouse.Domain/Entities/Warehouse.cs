namespace SmartWarehouse.Domain.Entities;

/// <summary>Склад или кладовая.</summary>
public class Warehouse
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public ICollection<StorageLocation> Locations { get; set; } = new List<StorageLocation>();
}

/// <summary>Место хранения: стеллаж, полка, ячейка. Код выводится в QR-метку.</summary>
public class StorageLocation
{
    public int Id { get; set; }

    public int WarehouseId { get; set; }

    public Warehouse Warehouse { get; set; } = null!;

    /// <summary>Уникальный код места (например, A-01-03). Содержимое QR-метки.</summary>
    public string Code { get; set; } = string.Empty;

    public string? Name { get; set; }
}
