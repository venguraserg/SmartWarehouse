namespace SmartWarehouse.Domain.Entities;

/// <summary>Номенклатура: запчасть или расходный материал.</summary>
public class Item
{
    public int Id { get; set; }

    /// <summary>Артикул / внутренний код. Уникален.</summary>
    public string Sku { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int? CategoryId { get; set; }

    public Category? Category { get; set; }

    public int UnitId { get; set; }

    public UnitOfMeasure Unit { get; set; } = null!;

    /// <summary>Минимальный остаток по всем местам. Ниже него позиция попадает в список дефицита.</summary>
    public decimal MinStock { get; set; }

    /// <summary>Желаемый максимальный остаток (до какого уровня пополнять).</summary>
    public decimal? MaxStock { get; set; }

    public string? Description { get; set; }

    /// <summary>Неактивные позиции скрыты из выбора, но остаются в истории.</summary>
    public bool IsActive { get; set; } = true;

    public ICollection<StockBalance> Balances { get; set; } = new List<StockBalance>();
}
