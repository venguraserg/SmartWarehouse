namespace SmartWarehouse.Domain.Entities;

/// <summary>Номенклатура: запчасть или расходный материал.</summary>
public class Item
{
    public int Id { get; set; }

    /// <summary>Артикул / внутренний код. Уникален.</summary>
    public string Sku { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Category { get; set; }

    /// <summary>Единица измерения (шт, м, кг, л).</summary>
    public string Unit { get; set; } = "шт";

    /// <summary>Минимальный остаток по всем местам. Ниже него позиция попадает в список закупки.</summary>
    public decimal MinStock { get; set; }

    /// <summary>Желаемый максимальный остаток (до какого уровня пополнять).</summary>
    public decimal? MaxStock { get; set; }

    public string? Description { get; set; }

    /// <summary>Неактивные позиции скрыты из выбора, но остаются в истории.</summary>
    public bool IsActive { get; set; } = true;

    public ICollection<StockBalance> Balances { get; set; } = new List<StockBalance>();
}
