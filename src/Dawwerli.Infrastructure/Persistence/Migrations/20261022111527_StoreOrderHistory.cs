using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dawwerli.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StoreOrderHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "store_order_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_store_order_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_store_order_events_store_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "store_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_store_order_events_order_id_at",
                table: "store_order_events",
                columns: new[] { "order_id", "at" });

            // Orders placed before the timeline existed: "new" at creation, plus their current status when it moved on.
            migrationBuilder.Sql("""
                INSERT INTO store_order_events (id, order_id, status, at)
                SELECT gen_random_uuid(), id, 0, created_at FROM store_orders;
                INSERT INTO store_order_events (id, order_id, status, at)
                SELECT gen_random_uuid(), id, status, status_changed_at FROM store_orders WHERE status <> 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "store_order_events");
        }
    }
}
