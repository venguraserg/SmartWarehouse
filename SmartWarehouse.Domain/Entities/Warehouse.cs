using SmartWarehouse.Domain.Enums;

namespace SmartWarehouse.Domain.Entities;

/// <summary>Склад или кладовая.</summary>
public class Warehouse
{
    public int Id { get; set; }

    /// <summary>Короткий код склада (например, СК1). Уникален, начинает полный код каждого места.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Все места хранения склада (на всех уровнях).</summary>
    public ICollection<StorageLocation> Locations { get; set; } = new List<StorageLocation>();
}

/// <summary>
/// Место хранения: зона, стеллаж, полка или ячейка. Образует дерево внутри склада.
/// Товар хранится только в конечных местах (у которых нет вложенных).
/// </summary>
public class StorageLocation
{
    public int Id { get; set; }

    public int WarehouseId { get; set; }

    public Warehouse Warehouse { get; set; } = null!;

    /// <summary>Родительское место (null — верхний уровень склада).</summary>
    public int? ParentId { get; set; }

    public StorageLocation? Parent { get; set; }

    public ICollection<StorageLocation> Children { get; set; } = new List<StorageLocation>();

    public LocationType Type { get; set; }

    /// <summary>Код внутри родителя: «A», «01», «03».</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Полный код от склада: «СК1-A-01-03». Уникален во всей системе,
    /// печатается в QR-метку и используется при сканировании.
    /// </summary>
    public string FullCode { get; set; } = string.Empty;

    public string? Name { get; set; }
}
