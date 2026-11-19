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

            // Данные: всё, что уже за сотрудниками, — «выдано в работу» (0), кроме подотчёта,
            // загруженного из 1С при вводе остатков (выдачи документом «Ввод остатков из 1С») — «по 1С» (1).
            // Движения возврата и списания этих же партий с того же сотрудника — тоже «по 1С».
            // Тип движения хранится строкой ('Issue'), вид подотчёта — числом.
            migrationBuilder.Sql("""
                UPDATE stock_movements SET "CustodyKind" = 0
                WHERE "FromEmployeeId" IS NOT NULL OR "ToEmployeeId" IS NOT NULL;

                UPDATE stock_movements m SET "CustodyKind" = 1
                FROM stock_documents d
                WHERE m."DocumentId" = d."Id" AND m."Type" = 'Issue' AND m."ToEmployeeId" IS NOT NULL
                  AND d."ExternalNumber" = 'Ввод остатков из 1С';

                UPDATE employee_balances b SET "Kind" = 1
                WHERE EXISTS (SELECT 1 FROM stock_movements m
                              WHERE m."CustodyKind" = 1 AND m."Type" = 'Issue'
                                AND m."ToEmployeeId" = b."EmployeeId" AND m."BatchId" = b."BatchId");

                UPDATE stock_movements r SET "CustodyKind" = 1
                WHERE r."FromEmployeeId" IS NOT NULL
                  AND EXISTS (SELECT 1 FROM stock_movements m
                              WHERE m."CustodyKind" = 1 AND m."Type" = 'Issue'
                                AND m."ToEmployeeId" = r."FromEmployeeId" AND m."BatchId" = r."BatchId");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Обратно — один подотчёт: строки одного сотрудника, позиции и партии разных видов складываются.
            migrationBuilder.Sql("""
                UPDATE employee_balances o SET "Quantity" = o."Quantity" + b."Quantity"
                FROM employee_balances b
                WHERE b."EmployeeId" = o."EmployeeId" AND b."ItemId" = o."ItemId" AND b."BatchId" = o."BatchId"
                  AND o."Kind" = 0 AND b."Kind" = 1;
                DELETE FROM employee_balances b
                WHERE b."Kind" = 1 AND EXISTS (SELECT 1 FROM employee_balances o
                    WHERE o."EmployeeId" = b."EmployeeId" AND o."ItemId" = b."ItemId" AND o."BatchId" = b."BatchId" AND o."Kind" = 0);
                """);

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
