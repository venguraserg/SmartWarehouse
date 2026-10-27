using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmartWarehouse.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "equipment",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ParentId = table.Column<int>(type: "integer", nullable: true),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_equipment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_equipment_equipment_ParentId",
                        column: x => x.ParentId,
                        principalTable: "equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "units",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsFractional = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_units", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "warehouses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_warehouses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stock_documents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExternalNumber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    EquipmentId = table.Column<int>(type: "integer", nullable: true),
                    PerformedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stock_documents_equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "items",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CategoryId = table.Column<int>(type: "integer", nullable: true),
                    UnitId = table.Column<int>(type: "integer", nullable: false),
                    MinStock = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    MaxStock = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ImageUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_items_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_items_units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "storage_locations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    ParentId = table.Column<int>(type: "integer", nullable: true),
                    Type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FullCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storage_locations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_storage_locations_storage_locations_ParentId",
                        column: x => x.ParentId,
                        principalTable: "storage_locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_storage_locations_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "item_images",
                columns: table => new
                {
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_images", x => x.ItemId);
                    table.ForeignKey(
                        name: "FK_item_images_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_balances",
                columns: table => new
                {
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_balances", x => new { x.ItemId, x.LocationId });
                    table.CheckConstraint("ck_stock_balances_quantity_nonnegative", "\"Quantity\" >= 0");
                    table.ForeignKey(
                        name: "FK_stock_balances_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_balances_storage_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "storage_locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentId = table.Column<long>(type: "bigint", nullable: false),
                    LineNo = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    FromLocationId = table.Column<int>(type: "integer", nullable: true),
                    ToLocationId = table.Column<int>(type: "integer", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_movements", x => x.Id);
                    table.CheckConstraint("ck_stock_movements_quantity_positive", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_stock_movements_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_movements_stock_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "stock_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_movements_storage_locations_FromLocationId",
                        column: x => x.FromLocationId,
                        principalTable: "storage_locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_movements_storage_locations_ToLocationId",
                        column: x => x.ToLocationId,
                        principalTable: "storage_locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_categories_Name",
                table: "categories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_equipment_Code",
                table: "equipment",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_equipment_ParentId",
                table: "equipment",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_items_CategoryId",
                table: "items",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_items_Sku",
                table: "items",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_items_UnitId",
                table: "items",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_balances_LocationId",
                table: "stock_balances",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_documents_EquipmentId",
                table: "stock_documents",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_documents_Type_OccurredAt",
                table: "stock_documents",
                columns: new[] { "Type", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_DocumentId_LineNo",
                table: "stock_movements",
                columns: new[] { "DocumentId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_FromLocationId",
                table: "stock_movements",
                column: "FromLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_ItemId_OccurredAt",
                table: "stock_movements",
                columns: new[] { "ItemId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_ToLocationId",
                table: "stock_movements",
                column: "ToLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_storage_locations_FullCode",
                table: "storage_locations",
                column: "FullCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_storage_locations_ParentId",
                table: "storage_locations",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_storage_locations_WarehouseId",
                table: "storage_locations",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_units_Code",
                table: "units",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_warehouses_Code",
                table: "warehouses",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "item_images");

            migrationBuilder.DropTable(
                name: "stock_balances");

            migrationBuilder.DropTable(
                name: "stock_movements");

            migrationBuilder.DropTable(
                name: "items");

            migrationBuilder.DropTable(
                name: "stock_documents");

            migrationBuilder.DropTable(
                name: "storage_locations");

            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropTable(
                name: "units");

            migrationBuilder.DropTable(
                name: "equipment");

            migrationBuilder.DropTable(
                name: "warehouses");
        }
    }
}
