using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotonBackend.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDisputeReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisputeReason",
                table: "Matches",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisputeReason",
                table: "Matches");
        }
    }
}
