using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartWarehouse.Domain.Entities;

namespace SmartWarehouse.Infrastructure.Data;

// Количества храним с точностью до тысячных (метры, килограммы, литры).
internal static class Precision
{
    public const int Total = 18;
    public const int Scale = 3;
}

internal class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> b)
    {
        b.ToTable("items");
        b.Property(x => x.Sku).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.Sku).IsUnique();
        b.Property(x => x.Name).HasMaxLength(256).IsRequired();
        b.Property(x => x.Category).HasMaxLength(128);
        b.Property(x => x.Unit).HasMaxLength(16).IsRequired();
        b.Property(x => x.MinStock).HasPrecision(Precision.Total, Precision.Scale);
        b.Property(x => x.MaxStock).HasPrecision(Precision.Total, Precision.Scale);
        b.Property(x => x.Description).HasMaxLength(2000);
    }
}

internal class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> b)
    {
        b.ToTable("warehouses");
        b.Property(x => x.Name).HasMaxLength(128).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.HasMany(x => x.Locations)
            .WithOne(x => x.Warehouse)
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal class StorageLocationConfiguration : IEntityTypeConfiguration<StorageLocation>
{
    public void Configure(EntityTypeBuilder<StorageLocation> b)
    {
        b.ToTable("storage_locations");
        b.Property(x => x.Code).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(128);
    }
}

internal class StockBalanceConfiguration : IEntityTypeConfiguration<StockBalance>
{
    public void Configure(EntityTypeBuilder<StockBalance> b)
    {
        b.ToTable("stock_balances", t => t.HasCheckConstraint("ck_stock_balances_quantity_nonnegative", "\"Quantity\" >= 0"));
        b.HasKey(x => new { x.ItemId, x.LocationId });
        b.Property(x => x.Quantity).HasPrecision(Precision.Total, Precision.Scale);

        b.HasOne(x => x.Item).WithMany(x => x.Balances).HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);

        // Защита от одновременной выдачи одного и того же остатка двумя пользователями:
        // Npgsql отображает uint-свойство с IsRowVersion() на системный столбец PostgreSQL xmin
        // (миграция отдельный столбец не создаёт).
        b.Property<uint>("Version").IsRowVersion();
    }
}

internal class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> b)
    {
        b.ToTable("stock_movements", t => t.HasCheckConstraint("ck_stock_movements_quantity_positive", "\"Quantity\" > 0"));
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.Quantity).HasPrecision(Precision.Total, Precision.Scale);
        b.Property(x => x.PerformedBy).HasMaxLength(128);
        b.Property(x => x.Document).HasMaxLength(128);
        b.Property(x => x.Comment).HasMaxLength(1000);

        b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.FromLocation).WithMany().HasForeignKey(x => x.FromLocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ToLocation).WithMany().HasForeignKey(x => x.ToLocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Equipment).WithMany().HasForeignKey(x => x.EquipmentId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.ItemId, x.OccurredAt });
    }
}

internal class EquipmentConfiguration : IEntityTypeConfiguration<Equipment>
{
    public void Configure(EntityTypeBuilder<Equipment> b)
    {
        b.ToTable("equipment");
        b.Property(x => x.Code).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(256).IsRequired();
        b.HasOne(x => x.Parent)
            .WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
