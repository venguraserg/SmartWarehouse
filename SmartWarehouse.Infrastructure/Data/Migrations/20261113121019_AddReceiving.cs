using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmartWarehouse.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OffBook",
                table: "batches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Origin",
                table: "batches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Партии, загруженные вводом остатков из 1С, — не «введены вручную» (иначе при приёме накладных
            // по ним будет ложное предупреждение о задвоении).
            migrationBuilder.Sql("""
                UPDATE batches b SET "Origin" = 2
                FROM stock_documents d
                WHERE d."Id" = b."ReceiptDocumentId" AND d."ExternalNumber" = 'Ввод остатков из 1С';
                """);

            migrationBuilder.CreateTable(
                name: "receipt_discrepancies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BatchId = table.Column<int>(type: "integer", nullable: false),
                    Expected = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Actual = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Resolution = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipt_discrepancies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_receipt_discrepancies_batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_receipt_discrepancies_BatchId",
                table: "receipt_discrepancies",
                column: "BatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "receipt_discrepancies");

            migrationBuilder.DropColumn(
                name: "OffBook",
                table: "batches");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "batches");
        }
    }
}
