namespace SmartWarehouse.Domain.Entities;

/// <summary>Сотрудник: проводит складские документы и получает запчасти.</summary>
public class Employee
{
    public int Id { get; set; }

    /// <summary>Табельный номер. Необязателен, но если указан — уникален.</summary>
    public string? PersonnelNumber { get; set; }

    public string LastName { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string? MiddleName { get; set; }

    public string? Position { get; set; }

    /// <summary>Подразделение, участок, смена.</summary>
    public string? Department { get; set; }

    public string? Phone { get; set; }

    /// <summary>Уволенные / неактивные скрыты из выбора в документах, но остаются в истории.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>«Иванов Иван Петрович».</summary>
    public string FullName => string.Join(' ', new[] { LastName, FirstName, MiddleName }.Where(p => !string.IsNullOrWhiteSpace(p)));

    /// <summary>«Иванов И. П.» — так сотрудник записывается в документы.</summary>
    public string ShortName =>
        $"{LastName} {Initial(FirstName)}{(string.IsNullOrWhiteSpace(MiddleName) ? "" : " " + Initial(MiddleName))}".Trim();

    private static string Initial(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "" : char.ToUpperInvariant(name.Trim()[0]) + ".";
}
