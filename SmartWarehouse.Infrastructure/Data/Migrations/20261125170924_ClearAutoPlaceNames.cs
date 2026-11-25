using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartWarehouse.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClearAutoPlaceNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Автоимена мест из импорта («1-й этаж», «Стеллаж C3», «Полка 1», «Ячейка 2») повторяют код и мешают
            // настоящему описанию — убираем. Подпись без описания строится из типа и кода. Своё описание не трогаем.
            migrationBuilder.Sql("""
                UPDATE storage_locations
                SET "Name" = NULL
                WHERE "Name" IS NOT NULL
                  AND (
                        btrim("Name") = ''
                     OR lower(btrim("Name")) = lower("Code")
                     OR ("Type" = 'Zone'  AND (lower(btrim("Name")) = lower('Зона ' || "Code")
                                              OR ("Code" = '1' AND lower(btrim("Name")) = '1-й этаж')
                                              OR ("Code" = '2' AND lower(btrim("Name")) = '2-й этаж')
                                              OR (upper("Code") = 'V' AND lower(btrim("Name")) = 'у ворот')))
                     OR ("Type" = 'Rack'  AND lower(btrim("Name")) = lower('Стеллаж ' || "Code"))
                     OR ("Type" = 'Shelf' AND lower(btrim("Name")) = lower('Полка ' || "Code"))
                     OR ("Type" = 'Cell'  AND lower(btrim("Name")) = lower('Ячейка ' || "Code"))
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Возвращать автоимена незачем: подпись строится из типа и кода.
        }
    }
}
