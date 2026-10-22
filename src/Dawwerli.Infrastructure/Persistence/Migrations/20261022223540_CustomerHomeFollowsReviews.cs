using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dawwerli.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerHomeFollowsReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "hidden_by_admin",
                table: "product_ratings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "featured_order",
                table: "pages",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "page_follows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_notified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_notification_id = table.Column<Guid>(type: "uuid", nullable: true),
                    window_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_page_follows", x => x.id);
                    table.ForeignKey(
                        name: "fk_page_follows_pages_page_id",
                        column: x => x.page_id,
                        principalTable: "pages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_product_ratings_product_id_hidden_by_admin",
                table: "product_ratings",
                columns: new[] { "product_id", "hidden_by_admin" });

            migrationBuilder.CreateIndex(
                name: "ix_pages_featured_order",
                table: "pages",
                column: "featured_order",
                unique: true,
                filter: "featured_order IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_page_follows_page_id",
                table: "page_follows",
                column: "page_id");

            migrationBuilder.CreateIndex(
                name: "ix_page_follows_user_id_page_id",
                table: "page_follows",
                columns: new[] { "user_id", "page_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "page_follows");

            migrationBuilder.DropIndex(
                name: "ix_product_ratings_product_id_hidden_by_admin",
                table: "product_ratings");

            migrationBuilder.DropIndex(
                name: "ix_pages_featured_order",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "hidden_by_admin",
                table: "product_ratings");

            migrationBuilder.DropColumn(
                name: "featured_order",
                table: "pages");
        }
    }
}
