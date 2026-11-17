using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartWarehouse.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustodyKinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_employee_balances",
                table: "employee_balances");

            migrationBuilder.AddColumn<int>(
                name: "CustodyKind",
                table: "stock_movements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "employee_balances",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_employee_balances",
                table: "employee_balances",
                columns: new[] { "EmployeeId", "ItemId", "BatchId", "Kind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_employee_balances",
                table: "employee_balances");

            migrationBuilder.DropColumn(
                name: "CustodyKind",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "employee_balances");

            migrationBuilder.AddPrimaryKey(
                name: "PK_employee_balances",
                table: "employee_balances",
                columns: new[] { "EmployeeId", "ItemId", "BatchId" });
        }
    }
}
