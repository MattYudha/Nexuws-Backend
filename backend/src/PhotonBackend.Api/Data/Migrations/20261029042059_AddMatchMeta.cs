using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotonBackend.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchMeta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HeroId",
                table: "MatchPlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "PartyId",
                table: "MatchPlayers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PartySize",
                table: "MatchPlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RatingBefore",
                table: "MatchPlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DurationSec",
                table: "Matches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "Matches",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HeroId",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "PartyId",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "PartySize",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "RatingBefore",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "DurationSec",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "Matches");
        }
    }
}
