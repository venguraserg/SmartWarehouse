namespace SmartWarehouse.Domain.Entities;

/// <summary>Единица технологического оборудования. Образует дерево: участок → установка → узел.</summary>
public class Equipment
{
    public int Id { get; set; }

    public int? ParentId { get; set; }

    public Equipment? Parent { get; set; }

    public ICollection<Equipment> Children { get; set; } = new List<Equipment>();

    /// <summary>Инвентарный или технологический номер. Уникален.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}
