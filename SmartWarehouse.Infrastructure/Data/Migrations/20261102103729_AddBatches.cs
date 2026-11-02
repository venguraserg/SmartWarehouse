using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmartWarehouse.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_stock_balances",
                table: "stock_balances");

            migrationBuilder.DropPrimaryKey(
                name: "PK_employee_balances",
                table: "employee_balances");

            migrationBuilder.AddColumn<int>(
                name: "BatchId",
                table: "write_off_act_lines",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BatchId",
                table: "stock_movements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BatchId",
                table: "stock_balances",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BatchId",
                table: "employee_balances",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_stock_balances",
                table: "stock_balances",
                columns: new[] { "ItemId", "LocationId", "BatchId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_employee_balances",
                table: "employee_balances",
                columns: new[] { "EmployeeId", "ItemId", "BatchId" });

            migrationBuilder.CreateTable(
                name: "batches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceiptDocumentId = table.Column<long>(type: "bigint", nullable: true),
                    Note = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_batches", x => x.Id);
                    table.CheckConstraint("ck_batches_price_nonnegative", "\"UnitPrice\" IS NULL OR \"UnitPrice\" >= 0");
                    table.ForeignKey(
                        name: "FK_batches_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_batches_stock_documents_ReceiptDocumentId",
                        column: x => x.ReceiptDocumentId,
                        principalTable: "stock_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_write_off_act_lines_BatchId",
                table: "write_off_act_lines",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_BatchId",
                table: "stock_movements",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_balances_BatchId",
                table: "stock_balances",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_employee_balances_BatchId",
                table: "employee_balances",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_batches_ItemId_ReceivedAt",
                table: "batches",
                columns: new[] { "ItemId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_batches_ReceiptDocumentId",
                table: "batches",
                column: "ReceiptDocumentId");

            // Остатки и движения до введения цен — по одной партии «без цены» на позицию;
            // цену такой партии администратор может указать в карточке позиции.
            migrationBuilder.Sql("""
                INSERT INTO batches ("ItemId", "UnitPrice", "ReceivedAt", "ReceiptDocumentId", "Note")
                SELECT i, NULL, now(), NULL, 'Остаток до введения цен'
                FROM (
                    SELECT "ItemId" AS i FROM stock_movements
                    UNION SELECT "ItemId" FROM stock_balances
                    UNION SELECT "ItemId" FROM employee_balances
                    UNION SELECT "ItemId" FROM write_off_act_lines
                ) used;

                UPDATE stock_movements m SET "BatchId" = b."Id" FROM batches b WHERE b."ItemId" = m."ItemId" AND m."BatchId" = 0;
                UPDATE stock_balances s SET "BatchId" = b."Id" FROM batches b WHERE b."ItemId" = s."ItemId" AND s."BatchId" = 0;
                UPDATE employee_balances e SET "BatchId" = b."Id" FROM batches b WHERE b."ItemId" = e."ItemId" AND e."BatchId" = 0;
                UPDATE write_off_act_lines l SET "BatchId" = b."Id" FROM batches b WHERE b."ItemId" = l."ItemId" AND l."BatchId" IS NULL;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_employee_balances_batches_BatchId",
                table: "employee_balances",
                column: "BatchId",
                principalTable: "batches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_balances_batches_BatchId",
                table: "stock_balances",
                column: "BatchId",
                principalTable: "batches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_movements_batches_BatchId",
                table: "stock_movements",
                column: "BatchId",
                principalTable: "batches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_write_off_act_lines_batches_BatchId",
                table: "write_off_act_lines",
                column: "BatchId",
                principalTable: "batches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_employee_balances_batches_BatchId",
                table: "employee_balances");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_balances_batches_BatchId",
                table: "stock_balances");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_movements_batches_BatchId",
                table: "stock_movements");

            migrationBuilder.DropForeignKey(
                name: "FK_write_off_act_lines_batches_BatchId",
                table: "write_off_act_lines");

            migrationBuilder.DropTable(
                name: "batches");

            migrationBuilder.DropIndex(
                name: "IX_write_off_act_lines_BatchId",
                table: "write_off_act_lines");

            migrationBuilder.DropIndex(
                name: "IX_stock_movements_BatchId",
                table: "stock_movements");

            migrationBuilder.DropPrimaryKey(
                name: "PK_stock_balances",
                table: "stock_balances");

            migrationBuilder.DropIndex(
                name: "IX_stock_balances_BatchId",
                table: "stock_balances");

            migrationBuilder.DropPrimaryKey(
                name: "PK_employee_balances",
                table: "employee_balances");

            migrationBuilder.DropIndex(
                name: "IX_employee_balances_BatchId",
                table: "employee_balances");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "write_off_act_lines");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "stock_balances");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "employee_balances");

            migrationBuilder.AddPrimaryKey(
                name: "PK_stock_balances",
                table: "stock_balances",
                columns: new[] { "ItemId", "LocationId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_employee_balances",
                table: "employee_balances",
                columns: new[] { "EmployeeId", "ItemId" });
        }
    }
}
