using Microsoft.EntityFrameworkCore;

namespace SmartWarehouse.Infrastructure.Data;

/// <summary>
/// Создание и обновление структуры базы при запуске API.
/// Самые первые версии программы были без миграций и создавали таблицы прямо по модели (EnsureCreated).
/// Такую базу первая миграция (InitialCreate) описывает один в один, поэтому она отмечается как уже применённая,
/// и дальше база обновляется обычными миграциями — данные, внесённые в ранней версии, сохраняются.
/// </summary>
public static class DatabaseSetup
{
    public static async Task MigrateAsync(WarehouseDbContext db, CancellationToken ct = default)
    {
        var migrations = db.Database.GetMigrations().ToList();
        if (migrations.Count == 0)
        {
            // Версия без миграций — таблицы по модели.
            await db.Database.EnsureCreatedAsync(ct);
            return;
        }

        if (await db.Database.CanConnectAsync(ct) && await CreatedWithoutMigrationsAsync(db, ct))
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (" +
                "\"MigrationId\" character varying(150) NOT NULL PRIMARY KEY, " +
                "\"ProductVersion\" character varying(32) NOT NULL)", ct);
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({0}, {1}) ON CONFLICT DO NOTHING",
                [migrations[0], "10.0.0"], ct);
        }

        await db.Database.MigrateAsync(ct);
    }

    /// <summary>Таблицы есть, а журнала миграций нет — база создана версией без миграций.</summary>
    private static async Task<bool> CreatedWithoutMigrationsAsync(WarehouseDbContext db, CancellationToken ct) =>
        await db.Database
            .SqlQueryRaw<bool>(
                "SELECT (to_regclass('public.items') IS NOT NULL AND to_regclass('public.\"__EFMigrationsHistory\"') IS NULL) AS \"Value\"")
            .SingleAsync(ct);
}
