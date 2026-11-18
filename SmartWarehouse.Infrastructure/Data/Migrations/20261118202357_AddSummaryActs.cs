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
