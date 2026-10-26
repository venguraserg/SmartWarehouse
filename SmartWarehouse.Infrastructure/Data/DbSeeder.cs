using Microsoft.EntityFrameworkCore;
using SmartWarehouse.Domain.Entities;

namespace SmartWarehouse.Infrastructure.Data;

/// <summary>Начальное заполнение справочников. Добавляет только то, чего ещё нет — безопасно запускать повторно.</summary>
public static class DbSeeder
{
    private static readonly UnitOfMeasure[] DefaultUnits =
    [
        new() { Code = "шт", Name = "штука", IsFractional = false },
        new() { Code = "компл", Name = "комплект", IsFractional = false },
        new() { Code = "упак", Name = "упаковка", IsFractional = false },
        new() { Code = "м", Name = "метр", IsFractional = true },
        new() { Code = "кг", Name = "килограмм", IsFractional = true },
        new() { Code = "л", Name = "литр", IsFractional = true }
    ];

    public static async Task SeedAsync(WarehouseDbContext db, CancellationToken ct = default)
    {
        var existing = await db.Units.Select(u => u.Code).ToListAsync(ct);

        foreach (var unit in DefaultUnits.Where(u => !existing.Contains(u.Code)))
            db.Units.Add(new UnitOfMeasure { Code = unit.Code, Name = unit.Name, IsFractional = unit.IsFractional });

        await db.SaveChangesAsync(ct);
    }
}
