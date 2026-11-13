using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotonBackend.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchEndReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReportedEndReason",
                table: "MatchPlayers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportedKoTimeline",
                table: "MatchPlayers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReportedWinnerSlot",
                table: "MatchPlayers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EndReason",
                table: "Matches",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsDraw",
                table: "Matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "KoTimeline",
                table: "Matches",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReportedEndReason",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "ReportedKoTimeline",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "ReportedWinnerSlot",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "EndReason",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "IsDraw",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "KoTimeline",
                table: "Matches");
        }
    }
}
