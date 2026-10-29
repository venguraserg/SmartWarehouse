using Microsoft.EntityFrameworkCore;
using SmartWarehouse.Domain.Entities;

namespace SmartWarehouse.Infrastructure.Data;

public class WarehouseDbContext(DbContextOptions<WarehouseDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<UnitOfMeasure> Units => Set<UnitOfMeasure>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<ItemImage> ItemImages => Set<ItemImage>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<StorageLocation> Locations => Set<StorageLocation>();
    public DbSet<StockBalance> Balances => Set<StockBalance>();
    public DbSet<StockDocument> Documents => Set<StockDocument>();
    public DbSet<StockMovement> Movements => Set<StockMovement>();
    public DbSet<Equipment> Equipment => Set<Equipment>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeeBalance> EmployeeBalances => Set<EmployeeBalance>();
    public DbSet<UserAccount> Users => Set<UserAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WarehouseDbContext).Assembly);
    }
}
