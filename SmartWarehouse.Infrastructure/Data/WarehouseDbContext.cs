using Microsoft.EntityFrameworkCore;
using SmartWarehouse.Domain.Entities;

namespace SmartWarehouse.Infrastructure.Data;

public class WarehouseDbContext(DbContextOptions<WarehouseDbContext> options) : DbContext(options)
{
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<StorageLocation> Locations => Set<StorageLocation>();
    public DbSet<StockBalance> Balances => Set<StockBalance>();
    public DbSet<StockMovement> Movements => Set<StockMovement>();
    public DbSet<Equipment> Equipment => Set<Equipment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WarehouseDbContext).Assembly);
    }
}
