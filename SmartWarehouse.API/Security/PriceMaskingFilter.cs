using System.Collections;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SmartWarehouse.API.Security;

/// <summary>
/// Цены и суммы видят все, кроме рядовых сотрудников. Для них в ответах API поля цен и стоимостей
/// (UnitPrice, Value, ValueIncomplete) очищаются — во всех DTO сразу, чтобы новая страница не «протекла».
/// </summary>
public class PriceMaskingFilter : IAsyncResultFilter
{
    private static readonly HashSet<string> PriceProperties = ["UnitPrice", "Value", "ValueIncomplete"];

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult { Value: { } value }
            && context.HttpContext.User.Identity?.IsAuthenticated == true
            && !context.HttpContext.User.SeesPrices())
        {
            Mask(value, depth: 0);
        }
        await next();
    }

    private static void Mask(object value, int depth)
    {
        if (depth > 6 || value is string)
            return;

        if (value is IEnumerable list)
        {
            foreach (var element in list)
                if (element is not null)
                    Mask(element, depth + 1);
            return;
        }

        var type = value.GetType();
        if (type.Namespace?.StartsWith("SmartWarehouse", StringComparison.Ordinal) != true)
            return;

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
                continue;

            if (PriceProperties.Contains(property.Name) && property.CanWrite)
            {
                // Записи (record) — свойства init, их можно задать через отражение.
                property.SetValue(value, property.PropertyType == typeof(bool) ? false : null);
                continue;
            }

            var propertyType = property.PropertyType;
            if (propertyType.IsValueType || propertyType == typeof(string))
                continue;
            if (property.GetValue(value) is { } nested)
                Mask(nested, depth + 1);
        }
    }
}
