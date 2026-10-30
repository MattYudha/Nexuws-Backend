using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotonBackend.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFactionAdminAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FactionAdminAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OldFaction = table.Column<int>(type: "integer", nullable: false),
                    NewFaction = table.Column<int>(type: "integer", nullable: false),
                    AdminSource = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FactionAdminAudits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FactionAdminAudits_At",
                table: "FactionAdminAudits",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_FactionAdminAudits_PlayerId",
                table: "FactionAdminAudits",
                column: "PlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FactionAdminAudits");
        }
    }
}
