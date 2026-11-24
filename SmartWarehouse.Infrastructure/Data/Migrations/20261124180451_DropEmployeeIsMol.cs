using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartWarehouse.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropEmployeeIsMol : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsMol",
                table: "employees");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsMol",
                table: "employees",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
