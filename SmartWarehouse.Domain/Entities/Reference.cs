namespace SmartWarehouse.Domain.Entities;

/// <summary>Категория номенклатуры (подшипники, уплотнения, электрика, смазки...).</summary>
public class Category
{
    public int Id { get; set; }

    /// <summary>Название. Уникально без учёта регистра.</summary>
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }
}

/// <summary>Единица измерения.</summary>
public class UnitOfMeasure
{
    public int Id { get; set; }

    /// <summary>Краткое обозначение: шт, м, кг, л, компл. Уникально.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Полное название: штука, метр, килограмм.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Допускается ли дробное количество. Для «шт» — нет (нельзя выдать 1,5 подшипника),
    /// для «м», «кг», «л» — да.
    /// </summary>
    public bool IsFractional { get; set; }
}
