using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartWarehouse.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class LatinCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SkuPrefix",
                table: "categories",
                type: "character varying(6)",
                maxLength: 6,
                nullable: true);

            // Коды с кириллицей → латиница (СК1 → WH1, ЗЧ- → SP-): их вводит ручной сканер при любой раскладке.
            migrationBuilder.Sql(SmartWarehouse.Infrastructure.Data.LatinCodesSql.Build());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SkuPrefix",
                table: "categories");
        }
    }
}
