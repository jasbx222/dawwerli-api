using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dawwerli.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StoreAndSellerPortal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "max_products",
                table: "pages",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    price = table.Column<long>(type: "bigint", nullable: false),
                    image_keys = table.Column<string[]>(type: "text[]", nullable: false),
                    category_id = table.Column<int>(type: "integer", nullable: false),
                    stock = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    hidden_by_admin = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                    table.CheckConstraint("ck_products_stock", "stock >= 0");
                    table.ForeignKey(
                        name: "fk_products_pages_page_id",
                        column: x => x.page_id,
                        principalTable: "pages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "seller_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    disabled = table.Column<bool>(type: "boolean", nullable: false),
                    failed_logins = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_login_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seller_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_seller_accounts_pages_page_id",
                        column: x => x.page_id,
                        principalTable: "pages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "store_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<long>(type: "bigint", nullable: false),
                    customer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    payment_method = table.Column<int>(type: "integer", nullable: false),
                    payment_status = table.Column<int>(type: "integer", nullable: false),
                    payment_reference = table.Column<string>(type: "text", nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    governorate_id = table.Column<int>(type: "integer", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    subtotal = table.Column<long>(type: "bigint", nullable: false),
                    commission_percent = table.Column<double>(type: "double precision", nullable: false),
                    commission = table.Column<long>(type: "bigint", nullable: false),
                    status_reason = table.Column<string>(type: "text", nullable: true),
                    status_changed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_store_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_store_orders_pages_page_id",
                        column: x => x.page_id,
                        principalTable: "pages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cart_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cart_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_cart_items_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "store_order_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    unit_price = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    image_key = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_store_order_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_store_order_items_store_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "store_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cart_items_product_id",
                table: "cart_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_cart_items_user_id_product_id",
                table: "cart_items",
                columns: new[] { "user_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_page_id_created_at",
                table: "products",
                columns: new[] { "page_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_seller_accounts_email",
                table: "seller_accounts",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_seller_accounts_page_id",
                table: "seller_accounts",
                column: "page_id");

            migrationBuilder.CreateIndex(
                name: "ix_store_order_items_order_id",
                table: "store_order_items",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_store_orders_customer_user_id_created_at",
                table: "store_orders",
                columns: new[] { "customer_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_store_orders_number",
                table: "store_orders",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_store_orders_page_id_status_created_at",
                table: "store_orders",
                columns: new[] { "page_id", "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cart_items");

            migrationBuilder.DropTable(
                name: "seller_accounts");

            migrationBuilder.DropTable(
                name: "store_order_items");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "store_orders");

            migrationBuilder.DropColumn(
                name: "max_products",
                table: "pages");
        }
    }
}
