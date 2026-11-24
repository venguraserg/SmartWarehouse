using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartWarehouse.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchMol : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsMol",
                table: "employees",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MolId",
                table: "batches",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_batches_MolId",
                table: "batches",
                column: "MolId");

            migrationBuilder.AddForeignKey(
                name: "FK_batches_employees_MolId",
                table: "batches",
                column: "MolId",
                principalTable: "employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // «Подотчёт по 1С» больше не ведётся: при запуске API он переносится на склад («Место не указано», пометка МОЛ).
            // Отправленные на утверждение акты по нему (Kind = 0) держат резерв — возвращаем их сотруднику с пояснением.
            migrationBuilder.Sql("""
                UPDATE write_off_acts
                SET "Status" = 'Returned',
                    "ReturnRemark" = 'Подотчёт по 1С больше не ведётся: позиции перенесены на склад с пометкой МОЛ. Акт не утверждается — его можно удалить. Списание — отчётом об использовании и сводным актом кладовщика.'
                WHERE "Kind" = 0 AND "Status" = 'Pending';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_batches_employees_MolId",
                table: "batches");

            migrationBuilder.DropIndex(
                name: "IX_batches_MolId",
                table: "batches");

            migrationBuilder.DropColumn(
                name: "IsMol",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "MolId",
                table: "batches");
        }
    }
}
