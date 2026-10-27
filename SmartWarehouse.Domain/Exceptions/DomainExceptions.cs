namespace SmartWarehouse.Domain.Exceptions;

/// <summary>Нарушение бизнес-правила (ошибка в данных запроса, недопустимая операция).</summary>
public class DomainException(string message) : Exception(message);

/// <summary>Запрошенный объект не найден.</summary>
public class NotFoundException(string message) : DomainException(message);

/// <summary>На месте хранения (или за сотрудником) недостаточно остатка для операции.</summary>
/// <param name="locationCode">Код места или, если <paramref name="isEmployee"/>, имя сотрудника.</param>
public class InsufficientStockException(string itemSku, string locationCode, decimal available, decimal requested, bool isEmployee = false)
    : DomainException(isEmployee
        ? $"Недостаточно: позиция {itemSku}, за сотрудником {locationCode} числится {available:0.###}, требуется {requested:0.###}."
        : $"Недостаточно остатка: позиция {itemSku}, место {locationCode}, в наличии {available:0.###}, требуется {requested:0.###}.")
{
    public string ItemSku { get; } = itemSku;
    public string LocationCode { get; } = locationCode;
    public decimal Available { get; } = available;
    public decimal Requested { get; } = requested;
}
