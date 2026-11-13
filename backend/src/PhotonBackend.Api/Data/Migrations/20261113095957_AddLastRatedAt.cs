using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotonBackend.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLastRatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastRatedAt",
                table: "Players",
                type: "timestamp with time zone",
                nullable: true);
            // D12: isi dari match ranked selesai terakhir (pemain lama ikut kena inflasi RD bila pasif).
            migrationBuilder.Sql(@"
UPDATE ""Players"" p SET ""LastRatedAt"" = x.last
FROM (SELECT mp.""PlayerId"", MAX(m.""EndedAt"") AS last
      FROM ""MatchPlayers"" mp JOIN ""Matches"" m ON m.""Id"" = mp.""MatchId""
      WHERE m.""Status"" = 'Finished' AND m.""Mode"" = 'ranked' AND NOT m.""IsDraw""
      GROUP BY mp.""PlayerId"") x
WHERE p.""Id"" = x.""PlayerId"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastRatedAt",
                table: "Players");
        }
    }
}
