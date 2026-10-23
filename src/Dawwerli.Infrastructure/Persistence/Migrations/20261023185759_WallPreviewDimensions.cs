using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dawwerli.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WallPreviewDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "depth_cm",
                table: "products",
                type: "numeric(5,1)",
                precision: 5,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "height_cm",
                table: "products",
                type: "numeric(5,1)",
                precision: 5,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "variant_group",
                table: "products",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "variant_label",
                table: "products",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "wall_kind",
                table: "products",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "width_cm",
                table: "products",
                type: "numeric(5,1)",
                precision: 5,
                scale: 1,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_page_id_variant_group",
                table: "products",
                columns: new[] { "page_id", "variant_group" },
                filter: "variant_group IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_products_size",
                table: "products",
                sql: "(width_cm IS NULL) = (height_cm IS NULL) AND (width_cm IS NULL OR (width_cm BETWEEN 1 AND 1000 AND height_cm BETWEEN 1 AND 1000)) AND (depth_cm IS NULL OR (width_cm IS NOT NULL AND depth_cm BETWEEN 1 AND 1000)) AND (wall_kind IS NULL OR width_cm IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_products_page_id_variant_group",
                table: "products");

            migrationBuilder.DropCheckConstraint(
                name: "ck_products_size",
                table: "products");

            migrationBuilder.DropColumn(
                name: "depth_cm",
                table: "products");

            migrationBuilder.DropColumn(
                name: "height_cm",
                table: "products");

            migrationBuilder.DropColumn(
                name: "variant_group",
                table: "products");

            migrationBuilder.DropColumn(
                name: "variant_label",
                table: "products");

            migrationBuilder.DropColumn(
                name: "wall_kind",
                table: "products");

            migrationBuilder.DropColumn(
                name: "width_cm",
                table: "products");
        }
    }
}
