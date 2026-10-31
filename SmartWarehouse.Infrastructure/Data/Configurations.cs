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

internal class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.ToTable("categories");
        b.Property(x => x.Name).HasMaxLength(128).IsRequired();
        b.HasIndex(x => x.Name).IsUnique();
        b.Property(x => x.Description).HasMaxLength(1000);
    }
}

internal class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    public void Configure(EntityTypeBuilder<UnitOfMeasure> b)
    {
        b.ToTable("units");
        b.Property(x => x.Code).HasMaxLength(16).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(64).IsRequired();
    }
}

internal class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> b)
    {
        b.ToTable("items");
        b.Property(x => x.Sku).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.Sku).IsUnique();
        b.Property(x => x.Name).HasMaxLength(256).IsRequired();
        b.Property(x => x.MinStock).HasPrecision(Precision.Total, Precision.Scale);
        b.Property(x => x.MaxStock).HasPrecision(Precision.Total, Precision.Scale);
        b.Property(x => x.Description).HasMaxLength(2000);

        b.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class ItemImageConfiguration : IEntityTypeConfiguration<ItemImage>
{
    public void Configure(EntityTypeBuilder<ItemImage> b)
    {
        b.ToTable("item_images");
        b.HasKey(x => x.ItemId);
        b.Property(x => x.ContentType).HasMaxLength(32).IsRequired();
        b.Property(x => x.Data).IsRequired();
        b.HasOne(x => x.Item).WithOne().HasForeignKey<ItemImage>(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> b)
    {
        b.ToTable("warehouses");
        b.Property(x => x.Code).HasMaxLength(16).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
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
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Code).HasMaxLength(16).IsRequired();
        b.Property(x => x.FullCode).HasMaxLength(128).IsRequired();
        b.HasIndex(x => x.FullCode).IsUnique();
        b.Property(x => x.Name).HasMaxLength(128);

        b.HasOne(x => x.Parent)
            .WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
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

        // Защита от одновременного расхода одного и того же остатка двумя пользователями:
        // Npgsql отображает uint-свойство с IsRowVersion() на системный столбец PostgreSQL xmin
        // (миграция отдельный столбец не создаёт).
        b.Property<uint>("Version").IsRowVersion();
    }
}

internal class StockDocumentConfiguration : IEntityTypeConfiguration<StockDocument>
{
    public void Configure(EntityTypeBuilder<StockDocument> b)
    {
        b.ToTable("stock_documents");
        b.Ignore(x => x.Number); // вычисляется из типа и Id
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.ExternalNumber).HasMaxLength(128);
        b.Property(x => x.PerformedBy).HasMaxLength(128);
        b.Property(x => x.Comment).HasMaxLength(1000);

        b.HasOne(x => x.Equipment).WithMany().HasForeignKey(x => x.EquipmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PerformedByEmployee).WithMany().HasForeignKey(x => x.PerformedById).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ReceivedByEmployee).WithMany().HasForeignKey(x => x.ReceivedById).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.ReceivedBy).HasMaxLength(128);
        b.HasIndex(x => x.ReceivedById);
        b.HasMany(x => x.Lines).WithOne(x => x.Document).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.Type, x.OccurredAt });
    }
}

internal class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> b)
    {
        b.ToTable("stock_movements", t => t.HasCheckConstraint("ck_stock_movements_quantity_positive", "\"Quantity\" > 0"));
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.Quantity).HasPrecision(Precision.Total, Precision.Scale);

        b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.FromLocation).WithMany().HasForeignKey(x => x.FromLocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ToLocation).WithMany().HasForeignKey(x => x.ToLocationId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.FromEmployee).WithMany().HasForeignKey(x => x.FromEmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ToEmployee).WithMany().HasForeignKey(x => x.ToEmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.FromEmployeeId);
        b.HasIndex(x => x.ToEmployeeId);

        b.HasIndex(x => new { x.DocumentId, x.LineNo }).IsUnique();
        b.HasIndex(x => new { x.ItemId, x.OccurredAt });
    }
}

internal class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> b)
    {
        b.ToTable("employees");
        b.Ignore(x => x.FullName);
        b.Ignore(x => x.ShortName);
        b.Property(x => x.PersonnelNumber).HasMaxLength(32);
        b.HasIndex(x => x.PersonnelNumber).IsUnique(); // NULL допускается у нескольких сотрудников
        b.Property(x => x.LastName).HasMaxLength(64).IsRequired();
        b.Property(x => x.FirstName).HasMaxLength(64).IsRequired();
        b.Property(x => x.MiddleName).HasMaxLength(64);
        b.Property(x => x.Position).HasMaxLength(128);
        b.Property(x => x.Department).HasMaxLength(128);
        b.Property(x => x.Phone).HasMaxLength(32);
        b.HasIndex(x => new { x.LastName, x.FirstName });
        b.HasOne(x => x.Manager).WithMany().HasForeignKey(x => x.ManagerId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class EmployeeBalanceConfiguration : IEntityTypeConfiguration<EmployeeBalance>
{
    public void Configure(EntityTypeBuilder<EmployeeBalance> b)
    {
        b.ToTable("employee_balances", t => t.HasCheckConstraint("ck_employee_balances_quantity_nonnegative", "\"Quantity\" >= 0"));
        b.HasKey(x => new { x.EmployeeId, x.ItemId });
        b.Property(x => x.Quantity).HasPrecision(Precision.Total, Precision.Scale);
        b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);

        // Защита от одновременного списания одного подотчёта (см. StockBalance).
        b.Property<uint>("Version").IsRowVersion();
    }
}

internal class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> b)
    {
        b.ToTable("users");
        b.Property(x => x.Login).HasMaxLength(32).IsRequired();
        b.HasIndex(x => x.Login).IsUnique();
        b.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
        b.Property(x => x.Roles).HasConversion<int>();
        b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        // У сотрудника — не больше одной учётной записи.
        b.HasIndex(x => x.EmployeeId).IsUnique();
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

internal class WriteOffActConfiguration : IEntityTypeConfiguration<WriteOffAct>
{
    public void Configure(EntityTypeBuilder<WriteOffAct> b)
    {
        b.ToTable("write_off_acts");
        b.Ignore(x => x.Number);
        b.Ignore(x => x.IsEditable);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.ExternalNumber).HasMaxLength(100);
        b.Property(x => x.Comment).HasMaxLength(1000);
        b.Property(x => x.CreatedBy).HasMaxLength(128);
        b.Property(x => x.DecidedBy).HasMaxLength(128);
        b.Property(x => x.DecidedByPosition).HasMaxLength(128);
        b.Property(x => x.ReturnRemark).HasMaxLength(1000);

        b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.DecidedByEmployee).WithMany().HasForeignKey(x => x.DecidedById).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Document).WithOne(x => x.Act).HasForeignKey<WriteOffAct>(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.DocumentId).IsUnique();
        b.HasIndex(x => new { x.EmployeeId, x.Status });
        b.HasIndex(x => x.Status);

        // Строки удаляются вместе с актом (и при замене строк в черновике).
        b.HasMany(x => x.Lines).WithOne(x => x.Act).HasForeignKey(x => x.ActId).OnDelete(DeleteBehavior.Cascade);

        // Защита от одновременного утверждения одного акта дважды.
        b.Property<uint>("Version").IsRowVersion();
    }
}

internal class WriteOffActLineConfiguration : IEntityTypeConfiguration<WriteOffActLine>
{
    public void Configure(EntityTypeBuilder<WriteOffActLine> b)
    {
        b.ToTable("write_off_act_lines", t => t.HasCheckConstraint("ck_write_off_act_lines_quantity_positive", "\"Quantity\" > 0"));
        b.Property(x => x.Quantity).HasPrecision(Precision.Total, Precision.Scale);
        b.Property(x => x.Usage).HasMaxLength(500);
        b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Equipment).WithMany().HasForeignKey(x => x.EquipmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ActId, x.LineNo }).IsUnique();
        b.HasIndex(x => x.ItemId);
    }
}
