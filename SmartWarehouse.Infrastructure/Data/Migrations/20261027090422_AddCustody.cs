using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartWarehouse.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustody : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FromEmployeeId",
                table: "stock_movements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ToEmployeeId",
                table: "stock_movements",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "employee_balances",
                columns: table => new
                {
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_balances", x => new { x.EmployeeId, x.ItemId });
                    table.CheckConstraint("ck_employee_balances_quantity_nonnegative", "\"Quantity\" >= 0");
                    table.ForeignKey(
                        name: "FK_employee_balances_employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_employee_balances_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_FromEmployeeId",
                table: "stock_movements",
                column: "FromEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_ToEmployeeId",
                table: "stock_movements",
                column: "ToEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_employee_balances_ItemId",
                table: "employee_balances",
                column: "ItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_stock_movements_employees_FromEmployeeId",
                table: "stock_movements",
                column: "FromEmployeeId",
                principalTable: "employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_movements_employees_ToEmployeeId",
                table: "stock_movements",
                column: "ToEmployeeId",
                principalTable: "employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_stock_movements_employees_FromEmployeeId",
                table: "stock_movements");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_movements_employees_ToEmployeeId",
                table: "stock_movements");

            migrationBuilder.DropTable(
                name: "employee_balances");

            migrationBuilder.DropIndex(
                name: "IX_stock_movements_FromEmployeeId",
                table: "stock_movements");

            migrationBuilder.DropIndex(
                name: "IX_stock_movements_ToEmployeeId",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "FromEmployeeId",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "ToEmployeeId",
                table: "stock_movements");
        }
    }
}
