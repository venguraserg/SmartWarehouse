using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmartWarehouse.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CheckedAt",
                table: "storage_locations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "inventories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RootLocationId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedById = table.Column<int>(type: "integer", nullable: true),
                    StartedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inventories_storage_locations_RootLocationId",
                        column: x => x.RootLocationId,
                        principalTable: "storage_locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_lines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InventoryId = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Expected = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Counted = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CountedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CountedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inventory_lines_inventories_InventoryId",
                        column: x => x.InventoryId,
                        principalTable: "inventories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_inventory_lines_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_inventory_lines_storage_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "storage_locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inventory_places",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InventoryId = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CheckedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_places", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inventory_places_inventories_InventoryId",
                        column: x => x.InventoryId,
                        principalTable: "inventories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_inventory_places_storage_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "storage_locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inventories_RootLocationId",
                table: "inventories",
                column: "RootLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_inventories_Status",
                table: "inventories",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_lines_InventoryId_LocationId_ItemId",
                table: "inventory_lines",
                columns: new[] { "InventoryId", "LocationId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_lines_ItemId",
                table: "inventory_lines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_lines_LocationId",
                table: "inventory_lines",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_places_InventoryId_LocationId",
                table: "inventory_places",
                columns: new[] { "InventoryId", "LocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_places_LocationId",
                table: "inventory_places",
                column: "LocationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_lines");

            migrationBuilder.DropTable(
                name: "inventory_places");

            migrationBuilder.DropTable(
                name: "inventories");

            migrationBuilder.DropColumn(
                name: "CheckedAt",
                table: "storage_locations");
        }
    }
}
