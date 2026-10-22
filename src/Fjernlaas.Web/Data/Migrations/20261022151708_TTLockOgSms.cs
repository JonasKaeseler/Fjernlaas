using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fjernlaas.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class TTLockOgSms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ForbindelseId",
                table: "Ressource",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TTLockLockId",
                table: "Ressource",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SmsNummer",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Navn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Nummer = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TokenPraefiks = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OprettetTid = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsNummer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SmsNummer_Organisation_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TTLockForbindelse",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Navn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TTLockBrugernavn = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    AdgangskodeHashBeskyttet = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AccessTokenBeskyttet = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RefreshTokenBeskyttet = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TokenUdloeberTid = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SidsteFejl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OprettetTid = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TTLockForbindelse", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TTLockForbindelse_Organisation_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Noegleord",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SmsNummerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RessourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ord = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OrdNormaliseret = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OprettetTid = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Noegleord", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Noegleord_Organisation_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Noegleord_Ressource_RessourceId",
                        column: x => x.RessourceId,
                        principalTable: "Ressource",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Noegleord_SmsNummer_SmsNummerId",
                        column: x => x.SmsNummerId,
                        principalTable: "SmsNummer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Oplaasningsforsoeg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TidspunktUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SmsNummerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RessourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Afsender = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Tekst = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Noegleord = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Udfald = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TTLockErrcode = table.Column<int>(type: "int", nullable: true),
                    Fejltekst = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Oplaasningsforsoeg", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Oplaasningsforsoeg_Organisation_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Oplaasningsforsoeg_Ressource_RessourceId",
                        column: x => x.RessourceId,
                        principalTable: "Ressource",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Oplaasningsforsoeg_SmsNummer_SmsNummerId",
                        column: x => x.SmsNummerId,
                        principalTable: "SmsNummer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Ressource_ForbindelseId",
                table: "Ressource",
                column: "ForbindelseId");

            migrationBuilder.CreateIndex(
                name: "IX_Noegleord_OrganisationId",
                table: "Noegleord",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_Noegleord_RessourceId",
                table: "Noegleord",
                column: "RessourceId");

            migrationBuilder.CreateIndex(
                name: "IX_Noegleord_SmsNummerId_OrdNormaliseret",
                table: "Noegleord",
                columns: new[] { "SmsNummerId", "OrdNormaliseret" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Oplaasningsforsoeg_OrganisationId_TidspunktUtc",
                table: "Oplaasningsforsoeg",
                columns: new[] { "OrganisationId", "TidspunktUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Oplaasningsforsoeg_RessourceId",
                table: "Oplaasningsforsoeg",
                column: "RessourceId");

            migrationBuilder.CreateIndex(
                name: "IX_Oplaasningsforsoeg_SmsNummerId_Afsender_TidspunktUtc",
                table: "Oplaasningsforsoeg",
                columns: new[] { "SmsNummerId", "Afsender", "TidspunktUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SmsNummer_OrganisationId",
                table: "SmsNummer",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_SmsNummer_TokenHash",
                table: "SmsNummer",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TTLockForbindelse_OrganisationId",
                table: "TTLockForbindelse",
                column: "OrganisationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Ressource_TTLockForbindelse_ForbindelseId",
                table: "Ressource",
                column: "ForbindelseId",
                principalTable: "TTLockForbindelse",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Ressource_TTLockForbindelse_ForbindelseId",
                table: "Ressource");

            migrationBuilder.DropTable(
                name: "Noegleord");

            migrationBuilder.DropTable(
                name: "Oplaasningsforsoeg");

            migrationBuilder.DropTable(
                name: "TTLockForbindelse");

            migrationBuilder.DropTable(
                name: "SmsNummer");

            migrationBuilder.DropIndex(
                name: "IX_Ressource_ForbindelseId",
                table: "Ressource");

            migrationBuilder.DropColumn(
                name: "ForbindelseId",
                table: "Ressource");

            migrationBuilder.DropColumn(
                name: "TTLockLockId",
                table: "Ressource");
        }
    }
}
