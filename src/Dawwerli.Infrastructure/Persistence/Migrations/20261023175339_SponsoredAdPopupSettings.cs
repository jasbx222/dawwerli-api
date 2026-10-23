using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dawwerli.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Sponsored ads are now shown as a timed popup. Adds the popup settings to the existing
    /// admin-editable settings row with their default values; keys the admin already set are kept.
    /// New databases get the same values from the "Business" configuration section on first seed.
    /// </summary>
    public partial class SponsoredAdPopupSettings : Migration
    {
        private const string Defaults = """{"PopupDurationSec": 5, "PopupIntervalMin": 10, "PopupFirstDelaySec": 3, "PopupMaxPerAdPerDay": 0}""";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"UPDATE settings SET data = '{Defaults}'::jsonb || data, updated_at = now() WHERE id = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE settings SET data = data - 'PopupDurationSec' - 'PopupIntervalMin' - 'PopupFirstDelaySec' - 'PopupMaxPerAdPerDay' WHERE id = 1;");
        }
    }
}
