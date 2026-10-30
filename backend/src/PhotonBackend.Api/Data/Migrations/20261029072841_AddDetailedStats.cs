using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotonBackend.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDetailedStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Criticals",
                table: "MatchPlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DamageDealt",
                table: "MatchPlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DamageTaken",
                table: "MatchPlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Dodges",
                table: "MatchPlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "FirstBlood",
                table: "MatchPlayers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "GearDrops",
                table: "MatchPlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GuardCrushes",
                table: "MatchPlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "HasDetailedStats",
                table: "MatchPlayers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "HeroSwitches",
                table: "MatchPlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HitsBlocked",
                table: "MatchPlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxDeficit",
                table: "MatchPlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxKillStreak",
                table: "MatchPlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ResultPayload",
                table: "MatchPlayers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SkillsHit",
                table: "MatchPlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SkillsUsed",
                table: "MatchPlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "MatchPlayerHeroes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    HeroId = table.Column<int>(type: "integer", nullable: false),
                    PlayTicks = table.Column<int>(type: "integer", nullable: false),
                    Kills = table.Column<int>(type: "integer", nullable: false),
                    Damage = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchPlayerHeroes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchPlayerHeroes_MatchPlayers_MatchId_PlayerId",
                        columns: x => new { x.MatchId, x.PlayerId },
                        principalTable: "MatchPlayers",
                        principalColumns: new[] { "MatchId", "PlayerId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatchPlayerHeroes_MatchId_PlayerId",
                table: "MatchPlayerHeroes",
                columns: new[] { "MatchId", "PlayerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchPlayerHeroes");

            migrationBuilder.DropColumn(
                name: "Criticals",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "DamageDealt",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "DamageTaken",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "Dodges",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "FirstBlood",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "GearDrops",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "GuardCrushes",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "HasDetailedStats",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "HeroSwitches",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "HitsBlocked",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "MaxDeficit",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "MaxKillStreak",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "ResultPayload",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "SkillsHit",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "SkillsUsed",
                table: "MatchPlayers");
        }
    }
}
