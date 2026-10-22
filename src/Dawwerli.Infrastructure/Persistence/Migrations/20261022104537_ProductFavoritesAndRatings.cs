using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dawwerli.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductFavoritesAndRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "rating",
                table: "products",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "rating_count",
                table: "products",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "product_favorites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_favorites", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_favorites_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_ratings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stars = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_ratings", x => x.id);
                    table.CheckConstraint("ck_product_ratings_stars", "stars BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_product_ratings_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_product_ratings_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_product_favorites_product_id",
                table: "product_favorites",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_favorites_user_id_created_at",
                table: "product_favorites",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_product_favorites_user_id_product_id",
                table: "product_favorites",
                columns: new[] { "user_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_ratings_product_id_updated_at",
                table: "product_ratings",
                columns: new[] { "product_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_product_ratings_product_id_user_id",
                table: "product_ratings",
                columns: new[] { "product_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_ratings_user_id",
                table: "product_ratings",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_favorites");

            migrationBuilder.DropTable(
                name: "product_ratings");

            migrationBuilder.DropColumn(
                name: "rating",
                table: "products");

            migrationBuilder.DropColumn(
                name: "rating_count",
                table: "products");
        }
    }
}
