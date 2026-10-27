using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmartWarehouse.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PerformedById",
                table: "stock_documents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceivedBy",
                table: "stock_documents",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReceivedById",
                table: "stock_documents",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "employees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PersonnelNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    LastName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MiddleName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Position = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Department = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employees", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stock_documents_PerformedById",
                table: "stock_documents",
                column: "PerformedById");

            migrationBuilder.CreateIndex(
                name: "IX_stock_documents_ReceivedById",
                table: "stock_documents",
                column: "ReceivedById");

            migrationBuilder.CreateIndex(
                name: "IX_employees_LastName_FirstName",
                table: "employees",
                columns: new[] { "LastName", "FirstName" });

            migrationBuilder.CreateIndex(
                name: "IX_employees_PersonnelNumber",
                table: "employees",
                column: "PersonnelNumber",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_documents_employees_PerformedById",
                table: "stock_documents",
                column: "PerformedById",
                principalTable: "employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_documents_employees_ReceivedById",
                table: "stock_documents",
                column: "ReceivedById",
                principalTable: "employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_stock_documents_employees_PerformedById",
                table: "stock_documents");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_documents_employees_ReceivedById",
                table: "stock_documents");

            migrationBuilder.DropTable(
                name: "employees");

            migrationBuilder.DropIndex(
                name: "IX_stock_documents_PerformedById",
                table: "stock_documents");

            migrationBuilder.DropIndex(
                name: "IX_stock_documents_ReceivedById",
                table: "stock_documents");

            migrationBuilder.DropColumn(
                name: "PerformedById",
                table: "stock_documents");

            migrationBuilder.DropColumn(
                name: "ReceivedBy",
                table: "stock_documents");

            migrationBuilder.DropColumn(
                name: "ReceivedById",
                table: "stock_documents");
        }
    }
}
