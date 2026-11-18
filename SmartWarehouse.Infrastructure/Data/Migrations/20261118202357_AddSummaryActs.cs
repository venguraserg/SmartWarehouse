using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartWarehouse.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSummaryActs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SummaryActId",
                table: "write_off_acts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DocumentId",
                table: "write_off_act_lines",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_write_off_acts_SummaryActId",
                table: "write_off_acts",
                column: "SummaryActId");

            migrationBuilder.CreateIndex(
                name: "IX_write_off_act_lines_DocumentId",
                table: "write_off_act_lines",
                column: "DocumentId");

            migrationBuilder.AddForeignKey(
                name: "FK_write_off_act_lines_stock_documents_DocumentId",
                table: "write_off_act_lines",
                column: "DocumentId",
                principalTable: "stock_documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_write_off_acts_write_off_acts_SummaryActId",
                table: "write_off_acts",
                column: "SummaryActId",
                principalTable: "write_off_acts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Позиции вне учёта в уже подтверждённых отчётах списаны при подтверждении —
            // помечаем их документом отчёта, чтобы они не попадали в сводный акт и не числились в резерве.
            migrationBuilder.Sql("""
                UPDATE write_off_act_lines l SET "DocumentId" = a."DocumentId"
                FROM write_off_acts a, batches b
                WHERE l."ActId" = a."Id" AND a."Kind" = 1 AND a."DocumentId" IS NOT NULL
                  AND l."BatchId" = b."Id" AND b."OffBook";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_write_off_act_lines_stock_documents_DocumentId",
                table: "write_off_act_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_write_off_acts_write_off_acts_SummaryActId",
                table: "write_off_acts");

            migrationBuilder.DropIndex(
                name: "IX_write_off_acts_SummaryActId",
                table: "write_off_acts");

            migrationBuilder.DropIndex(
                name: "IX_write_off_act_lines_DocumentId",
                table: "write_off_act_lines");

            migrationBuilder.DropColumn(
                name: "SummaryActId",
                table: "write_off_acts");

            migrationBuilder.DropColumn(
                name: "DocumentId",
                table: "write_off_act_lines");
        }
    }
}
