using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dawwerli.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OfferOrdersCouriersPins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "courier_id",
                table: "store_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "courier_name",
                table: "store_orders",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "courier_phone",
                table: "store_orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "kind",
                table: "store_orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "latitude",
                table: "store_orders",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "location_note",
                table: "store_orders",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "longitude",
                table: "store_orders",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "offer_order_id",
                table: "store_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "request_id",
                table: "store_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "couriers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_couriers", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_store_orders_offer_order_id",
                table: "store_orders",
                column: "offer_order_id",
                unique: true,
                filter: "offer_order_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_couriers_page_id",
                table: "couriers",
                column: "page_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "couriers");

            migrationBuilder.DropIndex(
                name: "ix_store_orders_offer_order_id",
                table: "store_orders");

            migrationBuilder.DropColumn(
                name: "courier_id",
                table: "store_orders");

            migrationBuilder.DropColumn(
                name: "courier_name",
                table: "store_orders");

            migrationBuilder.DropColumn(
                name: "courier_phone",
                table: "store_orders");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "store_orders");

            migrationBuilder.DropColumn(
                name: "latitude",
                table: "store_orders");

            migrationBuilder.DropColumn(
                name: "location_note",
                table: "store_orders");

            migrationBuilder.DropColumn(
                name: "longitude",
                table: "store_orders");

            migrationBuilder.DropColumn(
                name: "offer_order_id",
                table: "store_orders");

            migrationBuilder.DropColumn(
                name: "request_id",
                table: "store_orders");
        }
    }
}
