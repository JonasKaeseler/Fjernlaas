using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fjernlaas.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class LokationOgRessource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Lokation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Navn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NavnNormaliseret = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Adresse = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Tidszone = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OprettetTid = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lokation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Lokation_Organisation_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Ressource",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LokationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Navn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NavnNormaliseret = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OprettetTid = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ressource", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ressource_Lokation_LokationId",
                        column: x => x.LokationId,
                        principalTable: "Lokation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Ressource_Organisation_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Lokation_OrganisationId_NavnNormaliseret",
                table: "Lokation",
                columns: new[] { "OrganisationId", "NavnNormaliseret" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ressource_LokationId_NavnNormaliseret",
                table: "Ressource",
                columns: new[] { "LokationId", "NavnNormaliseret" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ressource_OrganisationId",
                table: "Ressource",
                column: "OrganisationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Ressource");

            migrationBuilder.DropTable(
                name: "Lokation");
        }
    }
}
