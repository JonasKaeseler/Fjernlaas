using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fjernlaas.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class BrugerSkalSkifteAdgangskode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SkalSkifteAdgangskode",
                table: "Bruger",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SkalSkifteAdgangskode",
                table: "Bruger");
        }
    }
}
