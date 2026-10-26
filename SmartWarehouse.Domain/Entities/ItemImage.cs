namespace SmartWarehouse.Domain.Entities;

/// <summary>Фото позиции номенклатуры (одно на позицию). Хранится в БД — попадает в обычный бэкап.</summary>
public class ItemImage
{
    /// <summary>Позиция; одновременно первичный ключ.</summary>
    public int ItemId { get; set; }

    public Item Item { get; set; } = null!;

    /// <summary>image/jpeg, image/png или image/webp.</summary>
    public string ContentType { get; set; } = string.Empty;

    public byte[] Data { get; set; } = [];
}
