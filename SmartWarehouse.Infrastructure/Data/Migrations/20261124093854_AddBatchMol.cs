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
