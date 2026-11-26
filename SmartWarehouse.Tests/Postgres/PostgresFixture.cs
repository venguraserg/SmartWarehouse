using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartWarehouse.Infrastructure.Data;

namespace SmartWarehouse.Tests.Postgres;

/// <summary>
/// Настоящий PostgreSQL для тестов: проверяем то, чего не видят подделки репозиториев —
/// перевод запросов в SQL, миграции, транзакции. Строка подключения — переменная SW_TEST_DB
/// (в CI — сервис postgres; локально без неё эти тесты пропускаются).
/// На каждый класс тестов — своя пустая база с применёнными миграциями, после — удаляется.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public static string? ServerConnectionString => Environment.GetEnvironmentVariable("SW_TEST_DB");

    public string ConnectionString { get; private set; } = "";

    private string _database = "";

    public async Task InitializeAsync()
    {
        if (string.IsNullOrEmpty(ServerConnectionString))
            return;
        _database = $"sw_test_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(ServerConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }
        ConnectionString = new NpgsqlConnectionStringBuilder(ServerConnectionString) { Database = _database }.ConnectionString;
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public WarehouseDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<WarehouseDbContext>().UseNpgsql(ConnectionString).Options);

    public async Task DisposeAsync()
    {
        if (string.IsNullOrEmpty(ServerConnectionString) || _database.Length == 0)
            return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(ServerConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }
}

/// <summary>Тест на настоящем PostgreSQL — пропускается, если не задана SW_TEST_DB.</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrEmpty(PostgresFixture.ServerConnectionString))
            Skip = "Не задана SW_TEST_DB — тесты на PostgreSQL пропущены.";
    }
}
