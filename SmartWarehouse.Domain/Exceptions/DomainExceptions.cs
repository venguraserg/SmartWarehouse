namespace SmartWarehouse.Domain.Exceptions;

/// <summary>Нарушение бизнес-правила (ошибка в данных запроса, недопустимая операция).</summary>
public class DomainException(string message) : Exception(message);

/// <summary>Запрошенный объект не найден.</summary>
public class NotFoundException(string message) : DomainException(message);

/// <summary>На месте хранения недостаточно остатка для операции.</summary>
public class InsufficientStockException(string itemSku, string locationCode, decimal available, decimal requested)
    : DomainException($"Недостаточно остатка: позиция {itemSku}, место {locationCode}, в наличии {available:0.###}, требуется {requested:0.###}.")
{
    public string ItemSku { get; } = itemSku;
    public string LocationCode { get; } = locationCode;
    public decimal Available { get; } = available;
    public decimal Requested { get; } = requested;
}
