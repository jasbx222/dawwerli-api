using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dawwerli.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// products.normalized_name: the search key for page product search, filled for existing rows here and on every save by
    /// Product.Name afterwards. Adds a pg_trgm GIN index when the extension can be enabled; without it search still works
    /// (a scan of one page's products), so a missing extension only logs a notice.
    /// </summary>
    public partial class PageProductSearch : Migration
    {
        // Frozen copy of Dawwerli.Domain.Rules.ArabicText at the time of this migration (a test keeps them in step).
        private const string From =
            "\u0623\u0625\u0622\u0671\u0629\u0649\u0626\u06CC\u0624\u06A9\u06AF" +
            "\u0660\u0661\u0662\u0663\u0664\u0665\u0666\u0667\u0668\u0669" +
            "\u06F0\u06F1\u06F2\u06F3\u06F4\u06F5\u06F6\u06F7\u06F8\u06F9" +
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
            "\t\n\v\f\r\u00A0";
        private const string To =
            "\u0627\u0627\u0627\u0627\u0647\u064A\u064A\u064A\u0648\u0643\u0643" +
            "0123456789" +
            "0123456789" +
            "abcdefghijklmnopqrstuvwxyz" +
            "      ";
        private const string Removed =
            "\u064B\u064C\u064D\u064E\u064F\u0650\u0651\u0652\u0653\u0654\u0655\u0656\u0657\u0658\u0659\u065A\u065B\u065C\u065D\u065E\u065F" +
            "\u0670\u0640";

        /// <summary>
        /// Escape-string literal with control characters written as \xHH: raw tabs and line breaks inside SQL text do not
        /// survive every client path (line endings get rewritten), which would shift translate()'s one-to-one mapping.
        /// </summary>
        private static string Literal(string s)
        {
            var sb = new System.Text.StringBuilder("E'");
            foreach (var c in s)
                sb.Append(c switch
                {
                    '\'' => "''",
                    '\\' => "\\\\",
                    < ' ' => $"\\x{(int)c:X2}",
                    _ => c.ToString(),
                });
            return sb.Append('\'').ToString();
        }

        /// <summary>SQL that folds <paramref name="column"/> exactly like ArabicText.Normalize.</summary>
        public static string NormalizeSql(string column) =>
            $"btrim(regexp_replace(translate({column}, {Literal(From + Removed)}, {Literal(To)}), ' {{2,}}', ' ', 'g'), ' ')";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "normalized_name",
                table: "products",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql($"UPDATE products SET normalized_name = {NormalizeSql("name")};");

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    BEGIN
                        CREATE EXTENSION IF NOT EXISTS pg_trgm;
                    EXCEPTION WHEN OTHERS THEN
                        RAISE NOTICE 'pg_trgm unavailable (%), product search runs without a trigram index', SQLERRM;
                    END;
                    IF EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pg_trgm') THEN
                        BEGIN
                            CREATE INDEX IF NOT EXISTS ix_products_normalized_name_trgm ON products USING gin (normalized_name gin_trgm_ops);
                        EXCEPTION WHEN OTHERS THEN
                            RAISE NOTICE 'trigram index not created (%)', SQLERRM;
                        END;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_products_normalized_name_trgm;");
            migrationBuilder.DropColumn(
                name: "normalized_name",
                table: "products");
        }
    }
}
