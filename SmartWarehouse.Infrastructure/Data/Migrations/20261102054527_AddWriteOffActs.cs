using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmartWarehouse.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWriteOffActs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Barcode",
                table: "items",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "write_off_acts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    PeriodFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    PeriodTo = table.Column<DateOnly>(type: "date", nullable: true),
                    ExternalNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecidedById = table.Column<int>(type: "integer", nullable: true),
                    DecidedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DecidedByPosition = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReturnRemark = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DocumentId = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_write_off_acts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_write_off_acts_employees_DecidedById",
                        column: x => x.DecidedById,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_write_off_acts_employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_write_off_acts_stock_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "stock_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "write_off_act_lines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ActId = table.Column<int>(type: "integer", nullable: false),
                    LineNo = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    EquipmentId = table.Column<int>(type: "integer", nullable: true),
                    Usage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UsedOn = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_write_off_act_lines", x => x.Id);
                    table.CheckConstraint("ck_write_off_act_lines_quantity_positive", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_write_off_act_lines_equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_write_off_act_lines_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_write_off_act_lines_write_off_acts_ActId",
                        column: x => x.ActId,
                        principalTable: "write_off_acts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_items_Barcode",
                table: "items",
                column: "Barcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_write_off_act_lines_ActId_LineNo",
                table: "write_off_act_lines",
                columns: new[] { "ActId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_write_off_act_lines_EquipmentId",
                table: "write_off_act_lines",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_write_off_act_lines_ItemId",
                table: "write_off_act_lines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_write_off_acts_DecidedById",
                table: "write_off_acts",
                column: "DecidedById");

            migrationBuilder.CreateIndex(
                name: "IX_write_off_acts_DocumentId",
                table: "write_off_acts",
                column: "DocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_write_off_acts_EmployeeId_Status",
                table: "write_off_acts",
                columns: new[] { "EmployeeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_write_off_acts_Status",
                table: "write_off_acts",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "write_off_act_lines");

            migrationBuilder.DropTable(
                name: "write_off_acts");

            migrationBuilder.DropIndex(
                name: "IX_items_Barcode",
                table: "items");

            migrationBuilder.DropColumn(
                name: "Barcode",
                table: "items");
        }
    }
}
