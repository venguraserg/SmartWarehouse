namespace SmartWarehouse.Domain.Exceptions;

/// <summary>Нарушение бизнес-правила (ошибка в данных запроса, недопустимая операция).</summary>
public class DomainException(string message) : Exception(message);

/// <summary>Неверный логин или пароль, заблокированная учётная запись.</summary>
public class AuthenticationFailedException(string message) : DomainException(message);

/// <summary>Недостаточно прав для операции.</summary>
public class ForbiddenException(string message) : DomainException(message);

/// <summary>Запрошенный объект не найден.</summary>
public class NotFoundException(string message) : DomainException(message);

/// <summary>На месте хранения (или за сотрудником) недостаточно остатка для операции.</summary>
/// <param name="locationCode">Код места или, если <paramref name="isEmployee"/>, имя сотрудника.</param>
/// <param name="available">Сколько можно взять (для сотрудника — без зарезервированного в актах на утверждении).</param>
/// <param name="reserved">Сколько за сотрудником зарезервировано актами на утверждении.</param>
public class InsufficientStockException(string itemSku, string locationCode, decimal available, decimal requested, bool isEmployee = false, decimal reserved = 0)
    : DomainException(Format(itemSku, locationCode, available, requested, isEmployee, reserved))
{
    public string ItemSku { get; } = itemSku;
    public string LocationCode { get; } = locationCode;
    public decimal Available { get; } = available;
    public decimal Requested { get; } = requested;
    public decimal Reserved { get; } = reserved;

    private static string Format(string sku, string where, decimal available, decimal requested, bool isEmployee, decimal reserved)
    {
        if (!isEmployee)
            return $"Недостаточно остатка: позиция {sku}, место {where}, в наличии {available:0.###}, требуется {requested:0.###}.";
        if (reserved <= 0)
            return $"Недостаточно: позиция {sku}, за сотрудником {where} числится {available:0.###}, требуется {requested:0.###}.";
        return $"Недостаточно: позиция {sku}, за сотрудником {where} числится {available + reserved:0.###}, " +
               $"из них {reserved:0.###} — в актах на утверждении; свободно {available:0.###}, требуется {requested:0.###}.";
    }
}
