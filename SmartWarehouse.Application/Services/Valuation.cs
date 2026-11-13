namespace SmartWarehouse.Application.Services;

/// <summary>Стоимость количества по партиям.</summary>
public static class Valuation
{
    /// <summary>
    /// Сумма «количество × цена партии». Если у части количества цена не указана (остатки до введения цен) —
    /// сумма по известным ценам и признак неполноты; если цен нет совсем — null.
    /// </summary>
    /// <summary>
    /// Цена партии для оценки: у партии вне учёта (б/у и т.п.) цена необязательна — без цены считается нулём
    /// и стоимость «неполной» не делает.
    /// </summary>
    public static decimal? Price(SmartWarehouse.Domain.Entities.Batch? batch) =>
        batch is null ? null : batch.OffBook ? batch.UnitPrice ?? 0 : batch.UnitPrice;

    public static (decimal? Value, bool Incomplete) Of(IEnumerable<(decimal Quantity, decimal? UnitPrice)> parts)
    {
        decimal sum = 0;
        var priced = false;
        var incomplete = false;
        foreach (var (quantity, price) in parts)
        {
            if (quantity == 0)
                continue;
            if (price is decimal p)
            {
                sum += quantity * p;
                priced = true;
            }
            else
            {
                incomplete = true;
            }
        }
        return (priced ? decimal.Round(sum, 2, MidpointRounding.AwayFromZero) : null, incomplete);
    }
}
