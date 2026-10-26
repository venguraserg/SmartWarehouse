namespace SmartWarehouse.Domain.Exceptions;

/// <summary>Нарушение бизнес-правила (ошибка в данных запроса, недопустимая операция).</summary>
public class DomainException(string message) : Exception(message);

/// <summary>Запрошенный объект не найден.</summary>
public class NotFoundException(string message) : DomainException(message);

/// <summary>На месте хранения недостаточно остатка для операции.</summary>
public class InsufficientStockException(int itemId, int locationId, decimal available, decimal requested)
    : DomainException($"Недостаточно остатка: позиция {itemId}, место {locationId}, в наличии {available}, требуется {requested}.")
{
    public int ItemId { get; } = itemId;
    public int LocationId { get; } = locationId;
    public decimal Available { get; } = available;
    public decimal Requested { get; } = requested;
}
